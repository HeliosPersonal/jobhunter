using System.Runtime.CompilerServices;
using System.Text.Json;
using JobHunter.Domain.Abstractions;

namespace JobHunter.TestKit;

/// <summary>
/// A fixture-driven <see cref="ILlmBatchClient"/> with zero network (testing conventions). It replays a
/// pre-loaded result set, counts every call by method, and can be put into a <em>throw-on-submit</em>
/// mode — which is what turns the cost-ceiling test into an <em>absence</em> assertion (QG-2): the test
/// passes only if <see cref="SubmitAsync"/> is never invoked, which is strictly stronger than asserting
/// a resulting state.
///
/// <para>Status polling is deterministic and clock-driven: the client reports
/// <see cref="ProviderBatchState.InProgress"/> for the first <see cref="PollsBeforeEnd"/> calls and
/// <see cref="ProviderBatchState.Ended"/> thereafter, so a test can drive the whole poll/backoff
/// schedule through <see cref="FakeClock"/> without waiting on real time.</para>
/// </summary>
public sealed class FakeLlmBatchClient : ILlmBatchClient
{
    private readonly List<BatchResultItem> _results;
    private readonly ProviderBatchState _terminalState;

    public FakeLlmBatchClient(
        IEnumerable<BatchResultItem>? results = null,
        int pollsBeforeEnd = 0,
        ProviderBatchState terminalState = ProviderBatchState.Ended)
    {
        _results = results?.ToList() ?? [];
        PollsBeforeEnd = pollsBeforeEnd;
        _terminalState = terminalState;
        ProviderBatchId = "msgbatch_fake_0001";
    }

    /// <summary>
    /// Builds a client that replays a JSONL fixture (SAD §5, test-plan §Test data). Each line is an
    /// envelope — <c>custom_id</c>, and exactly one of a <c>result</c> object (the tool-use payload,
    /// re-serialised verbatim into <see cref="BatchResultItem.RawJson"/>) or a <c>provider_error</c>
    /// string — plus optional <c>input_tokens</c>/<c>output_tokens</c>. Blank lines are ignored, so a
    /// hand-edited fixture is forgiving.
    /// </summary>
    public static FakeLlmBatchClient FromJsonlFile(
        string path,
        int pollsBeforeEnd = 0,
        ProviderBatchState terminalState = ProviderBatchState.Ended)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var lines = File.ReadAllLines(path);
        return FromJsonlLines(lines, pollsBeforeEnd, terminalState);
    }

    /// <summary>Replays fixture <paramref name="lines"/> already in memory (see <see cref="FromJsonlFile"/>).</summary>
    public static FakeLlmBatchClient FromJsonlLines(
        IEnumerable<string> lines,
        int pollsBeforeEnd = 0,
        ProviderBatchState terminalState = ProviderBatchState.Ended)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var items = new List<BatchResultItem>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            var customId = root.GetProperty("custom_id").GetString()
                ?? throw new FormatException("A fixture line is missing custom_id.");

            string? rawJson = null;
            if (root.TryGetProperty("result", out var result) && result.ValueKind != JsonValueKind.Null)
            {
                rawJson = result.GetRawText();
            }

            string? providerError = null;
            if (root.TryGetProperty("provider_error", out var err) && err.ValueKind == JsonValueKind.String)
            {
                providerError = err.GetString();
            }

            var inputTokens = root.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0;
            var outputTokens = root.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0;
            var cacheRead = root.TryGetProperty("cache_read_input_tokens", out var cr) ? cr.GetInt32() : 0;

            items.Add(new BatchResultItem(
                customId, rawJson, providerError, new TokenUsage(inputTokens, outputTokens, cacheRead)));
        }

        return new FakeLlmBatchClient(items, pollsBeforeEnd, terminalState);
    }

    /// <summary>When true, any <see cref="SubmitAsync"/> call throws — the ceiling guard's tripwire.</summary>
    public bool ThrowOnSubmit { get; set; }

    /// <summary>An artificial per-call delay, so a test can exercise timeouts without real latency.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    /// <summary>Number of status polls that report <see cref="ProviderBatchState.InProgress"/> before ending.</summary>
    public int PollsBeforeEnd { get; set; }

    /// <summary>The provider batch id handed back from <see cref="SubmitAsync"/>.</summary>
    public string ProviderBatchId { get; set; }

    public int SubmitCallCount { get; private set; }

    public int StatusCallCount { get; private set; }

    public int ResultsCallCount { get; private set; }

    public int ListCallCount { get; private set; }

    /// <summary>The most recent submission, so a test can assert item count, tier and prompt version.</summary>
    public BatchSubmission? LastSubmission { get; private set; }

    /// <summary>
    /// The provider-side view every accepted submission leaves behind, so the reconciliation read can find
    /// a batch the provider already holds even when the local batch row never committed (SAD §11 D5,
    /// crash-matrix checkpoint 4). A test seeds <see cref="ProviderCreatedAt"/> before submitting so the
    /// created-at bound reconciliation applies is deterministic under <see cref="FakeClock"/>.
    /// </summary>
    private readonly List<ProviderBatchRef> _providerBatches = [];

    /// <summary>The created-at stamped on the next accepted submission's provider-side record.</summary>
    public DateTimeOffset ProviderCreatedAt { get; set; } = FakeClock.DefaultNow;

    public async Task<string> SubmitAsync(BatchSubmission submission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        SubmitCallCount++;

        if (ThrowOnSubmit)
        {
            // The tripwire: a ceiling test proves the client was never reached, not merely that a state changed.
            throw new InvalidOperationException(
                "FakeLlmBatchClient.SubmitAsync was called while ThrowOnSubmit was set — the cost ceiling did not gate the submission (QG-2).");
        }

        LastSubmission = submission;
        // The provider now holds this batch, whether or not the caller lives long enough to persist its id.
        // A reconciling restart finds it here, which is the whole point of checkpoint 4.
        _providerBatches.Add(new ProviderBatchRef(ProviderBatchId, ProviderCreatedAt));
        await MaybeDelayAsync(cancellationToken).ConfigureAwait(false);
        return ProviderBatchId;
    }

    public async Task<IReadOnlyList<ProviderBatchRef>> ListRecentBatchesAsync(
        DateTimeOffset createdOnOrAfter,
        CancellationToken cancellationToken)
    {
        ListCallCount++;
        await MaybeDelayAsync(cancellationToken).ConfigureAwait(false);
        return _providerBatches
            .Where(b => b.CreatedAt >= createdOnOrAfter)
            .OrderByDescending(b => b.CreatedAt)
            .ToList();
    }

    public async Task<BatchStatus> GetStatusAsync(string providerBatchId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerBatchId);
        StatusCallCount++;
        await MaybeDelayAsync(cancellationToken).ConfigureAwait(false);

        var ended = StatusCallCount > PollsBeforeEnd;
        if (!ended)
        {
            return new BatchStatus(ProviderBatchState.InProgress, 0, 0, _results.Count);
        }

        var succeeded = _results.Count(r => r.RawJson is not null);
        var errored = _results.Count(r => r.ProviderError is not null);
        return new BatchStatus(_terminalState, succeeded, errored, 0);
    }

    public async IAsyncEnumerable<BatchResultItem> GetResultsAsync(
        string providerBatchId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerBatchId);
        ResultsCallCount++;

        foreach (var item in _results)
        {
            await MaybeDelayAsync(cancellationToken).ConfigureAwait(false);
            yield return item;
        }
    }

    private async Task MaybeDelayAsync(CancellationToken cancellationToken)
    {
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
        }
    }
}
