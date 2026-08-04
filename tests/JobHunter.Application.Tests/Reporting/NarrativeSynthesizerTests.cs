using JobHunter.Application.Reporting;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Reporting;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Reporting;

/// <summary>
/// T05: the bounded best-effort market-note synthesiser (ADR-F5-0001, Option A). It is priced, ceiling-checked
/// and ledgered <em>exactly</em> like every other batch (invariant 6) — the ceiling gate is proven as an
/// <em>absence</em> with <see cref="FakeLlmBatchClient.ThrowOnSubmit"/>, the same tripwire enrichment and
/// matching use (QG-2). Everything else asserts the one promise the digest depends on: whatever goes wrong —
/// a dead day, a ceiling breach, a provider outage, a transport fault, a slow batch, a parse miss — the
/// synthesiser <strong>returns a <see cref="NarrativeResult"/>, never throws, and never blocks past its
/// budget</strong>, falling back to the deterministic template so the 07:00 digest still ships. The repository,
/// request builder, result parser and cost accountant are substituted, so these are zero-database, zero-network
/// unit tests.
/// </summary>
public sealed class NarrativeSynthesizerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 2, 5, 0, TimeSpan.Zero);
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");

    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly INarrativeRequestBuilder _requestBuilder = Substitute.For<INarrativeRequestBuilder>();
    private readonly INarrativeResultParser _resultParser = Substitute.For<INarrativeResultParser>();
    private readonly ICostAccountant _accountant = Substitute.For<ICostAccountant>();
    private readonly FakeLlmBatchClient _client = new(
        results: [new BatchResultItem("digest-narrative", """{"narrative":"ignored — the parser is substituted"}""", null, new TokenUsage(120, 60))]);
    private readonly FakeClock _clock = new(Now);
    private readonly SequentialIdGenerator _ids = new();
    private readonly NarrativeSynthesisOptions _options = new();

    public NarrativeSynthesizerTests()
    {
        // A live day by default: the request renders one item, the estimate fits a generous ceiling, and the
        // provider's single result parses into a model note. Tests that probe a fallback override one of these.
        _requestBuilder.Build(Arg.Any<NarrativeInput>()).Returns(_ => Request());
        _accountant.Estimate(Arg.Any<ModelTier>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>())
            .Returns(new CostEstimate(0.05m, 100, 50));
        _accountant.Actual(Arg.Any<ModelTier>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(new CostEstimate(0.04m, 120, 60));
        _resultParser.Parse(Arg.Any<string?>()).Returns(NarrativeParseOutcome.Success("A calm market: six roles worth a look."));
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(LiveRun());
        _runs.FindBatchAsync(RunId, BatchStage.Synthesis, ModelTier.Deep, Arg.Any<CancellationToken>())
            .Returns((Batch?)null);
    }

    private NarrativeSynthesizer CreateSynthesizer(
        ILlmBatchClient? client = null, NarrativeSynthesisOptions? options = null) =>
        new(_runs, _requestBuilder, _resultParser, _accountant, client ?? _client, _clock, _ids,
            options ?? _options, NullLogger<NarrativeSynthesizer>.Instance);

    private static NarrativeInput LiveDay() =>
        new(TotalNewJobs: 42, StrongMatches: 6, CardCount: 5, AvgSalaryUsd: 145000m,
            SuppressedCount: 11, CarriedOverCount: 3, DegradedSourceCount: 1);

    private static NarrativeInput DeadDay() =>
        new(TotalNewJobs: 0, StrongMatches: 0, CardCount: 0, AvgSalaryUsd: null,
            SuppressedCount: 0, CarriedOverCount: 0, DegradedSourceCount: 0);

    private static NarrativeBatchRequest Request() =>
        new("digest-narrative-v1",
            [new BatchRequestItem("digest-narrative", "system", "facts", new JsonSchema("record_market_note", "{}"))],
            MaxOutputTokensPerItem: 400);

    private static Run LiveRun(decimal ceiling = 2.00m, decimal spent = 0m)
    {
        var run = new Run(RunId, Now.AddHours(-24), Now, ceiling, Now.AddHours(-3));
        if (spent > 0m)
        {
            run.SetSpend(spent);
        }

        return run;
    }

    // ---- The happy path: a model note, stamped and ledgered like any other batch ---------------

    [Fact]
    public async Task A_landed_model_note_is_returned_as_a_model_result_stamped_with_the_prompt_version()
    {
        var result = await CreateSynthesizer().SynthesizeAsync(RunId, LiveDay(), CancellationToken.None);

        result.Source.ShouldBe(NarrativeSource.Model);
        result.Narrative.ShouldBe("A calm market: six roles worth a look.");
        result.PromptVersion.ShouldBe("digest-narrative-v1");
        _client.SubmitCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_landed_model_note_writes_both_the_estimated_and_the_actual_ledger_entry()
    {
        await CreateSynthesizer().SynthesizeAsync(RunId, LiveDay(), CancellationToken.None);

        _runs.Received().AddLedgerEntry(Arg.Is<CostLedgerEntry>(e => e != null &&
            e.Stage == BatchStage.Synthesis && e.Tier == ModelTier.Deep && e.Kind == LedgerEntryKind.Estimated));
        _runs.Received().AddLedgerEntry(Arg.Is<CostLedgerEntry>(e => e != null &&
            e.Stage == BatchStage.Synthesis && e.Tier == ModelTier.Deep && e.Kind == LedgerEntryKind.Actual));
    }

    [Fact]
    public async Task A_landed_model_note_submits_a_single_deep_tier_synthesis_item()
    {
        await CreateSynthesizer().SynthesizeAsync(RunId, LiveDay(), CancellationToken.None);

        var submission = _client.LastSubmission.ShouldNotBeNull();
        submission.Tier.ShouldBe(ModelTier.Deep);
        submission.PromptVersion.ShouldBe("digest-narrative-v1");
        submission.Items.Count.ShouldBe(1);
    }

    // ---- QG-2 / invariant 6: the ceiling is a precondition, the client is never reached --------

    [Fact]
    public async Task An_estimate_that_would_breach_the_ceiling_never_calls_the_client_and_uses_the_template()
    {
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(LiveRun(ceiling: 0.01m));
        _client.ThrowOnSubmit = true; // the tripwire: the test passes only if SubmitAsync is never reached.

        var result = await CreateSynthesizer().SynthesizeAsync(RunId, LiveDay(), CancellationToken.None);

        result.Source.ShouldBe(NarrativeSource.Template);
        _client.SubmitCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_ceiling_breach_records_no_ledger_estimate_and_no_batch()
    {
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(LiveRun(ceiling: 0.01m));
        _client.ThrowOnSubmit = true;

        await CreateSynthesizer().SynthesizeAsync(RunId, LiveDay(), CancellationToken.None);

        _runs.DidNotReceive().AddLedgerEntry(Arg.Any<CostLedgerEntry>());
        _runs.DidNotReceive().AddBatch(Arg.Any<Batch>());
    }

    [Fact]
    public async Task Spend_already_incurred_this_run_counts_toward_the_ceiling()
    {
        // The estimate alone (0.05) fits under 0.06, but the run has already spent 0.04 — the projection breaches.
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(LiveRun(ceiling: 0.06m, spent: 0.04m));
        _client.ThrowOnSubmit = true;

        var result = await CreateSynthesizer().SynthesizeAsync(RunId, LiveDay(), CancellationToken.None);

        result.Source.ShouldBe(NarrativeSource.Template);
        _client.SubmitCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task The_estimate_is_ledgered_and_committed_before_the_client_is_called()
    {
        var events = new List<string>();
        _runs.When(r => r.AddLedgerEntry(Arg.Is<CostLedgerEntry>(e => e != null && e.Kind == LedgerEntryKind.Estimated)))
            .Do(_ => events.Add("ledger"));
        _runs.When(r => r.SaveChangesAsync(Arg.Any<CancellationToken>())).Do(_ => events.Add("save"));

        await CreateSynthesizer(client: new RecordingClient(events))
            .SynthesizeAsync(RunId, LiveDay(), CancellationToken.None);

        var ledgerIndex = events.IndexOf("ledger");
        var firstSaveAfterLedger = events.FindIndex(ledgerIndex, e => e == "save");
        var submitIndex = events.IndexOf("submit");
        ledgerIndex.ShouldBeGreaterThanOrEqualTo(0);
        submitIndex.ShouldBeGreaterThan(firstSaveAfterLedger);
    }

    // ---- Best-effort: every miss falls back to the template, never throws ----------------------

    [Fact]
    public async Task A_dead_day_uses_the_template_and_never_looks_up_the_run_or_calls_the_client()
    {
        var result = await CreateSynthesizer().SynthesizeAsync(RunId, DeadDay(), CancellationToken.None);

        result.Source.ShouldBe(NarrativeSource.Template);
        result.PromptVersion.ShouldBeNull();
        _client.SubmitCallCount.ShouldBe(0);
        await _runs.DidNotReceive().FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_missing_run_uses_the_template_rather_than_throwing()
    {
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns((Run?)null);

        var result = await CreateSynthesizer().SynthesizeAsync(RunId, LiveDay(), CancellationToken.None);

        result.Source.ShouldBe(NarrativeSource.Template);
        _client.SubmitCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_provider_fault_uses_the_template_rather_than_failing_the_digest()
    {
        var faulting = new ThrowingClient(new AdapterFault("the provider returned 503"));

        var result = await CreateSynthesizer(client: faulting).SynthesizeAsync(RunId, LiveDay(), CancellationToken.None);

        result.Source.ShouldBe(NarrativeSource.Template);
    }

    [Fact]
    public async Task A_transport_fault_uses_the_template_rather_than_failing_the_digest()
    {
        var faulting = new ThrowingClient(new HttpRequestException("connection reset"));

        var result = await CreateSynthesizer(client: faulting).SynthesizeAsync(RunId, LiveDay(), CancellationToken.None);

        result.Source.ShouldBe(NarrativeSource.Template);
    }

    [Fact]
    public async Task A_batch_that_does_not_end_within_the_budget_uses_the_template()
    {
        // The budget elapses while the client is still delaying, so the linked token cancels the poll — the
        // note is abandoned and the digest ships from the template (ADR-F5-0001).
        var slow = new FakeLlmBatchClient(pollsBeforeEnd: 1000) { Delay = TimeSpan.FromMilliseconds(200) };
        var tight = new NarrativeSynthesisOptions
        {
            Timeout = TimeSpan.FromMilliseconds(20),
            PollInterval = TimeSpan.FromMilliseconds(5),
        };

        var result = await CreateSynthesizer(client: slow, options: tight)
            .SynthesizeAsync(RunId, LiveDay(), CancellationToken.None);

        result.Source.ShouldBe(NarrativeSource.Template);
    }

    [Fact]
    public async Task A_caller_cancellation_uses_the_template_rather_than_throwing()
    {
        var slow = new FakeLlmBatchClient(pollsBeforeEnd: 1000) { Delay = TimeSpan.FromMilliseconds(200) };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await CreateSynthesizer(client: slow).SynthesizeAsync(RunId, LiveDay(), cts.Token);

        result.Source.ShouldBe(NarrativeSource.Template);
    }

    [Fact]
    public async Task A_provider_side_cancelled_batch_uses_the_template()
    {
        var cancelled = new FakeLlmBatchClient(terminalState: ProviderBatchState.Cancelled);

        var result = await CreateSynthesizer(client: cancelled).SynthesizeAsync(RunId, LiveDay(), CancellationToken.None);

        result.Source.ShouldBe(NarrativeSource.Template);
    }

    [Fact]
    public async Task A_result_that_errored_at_the_provider_uses_the_template_but_still_bills_the_actual()
    {
        var errored = new FakeLlmBatchClient(
            results: [new BatchResultItem("digest-narrative", null, "overloaded_error", new TokenUsage(120, 0))]);

        var result = await CreateSynthesizer(client: errored).SynthesizeAsync(RunId, LiveDay(), CancellationToken.None);

        result.Source.ShouldBe(NarrativeSource.Template);
        _runs.Received().AddLedgerEntry(Arg.Is<CostLedgerEntry>(e => e != null && e.Kind == LedgerEntryKind.Actual));
    }

    [Fact]
    public async Task A_result_that_does_not_parse_uses_the_template_but_still_bills_the_actual()
    {
        _resultParser.Parse(Arg.Any<string?>()).Returns(NarrativeParseOutcome.Failure("blank narrative"));

        var result = await CreateSynthesizer().SynthesizeAsync(RunId, LiveDay(), CancellationToken.None);

        result.Source.ShouldBe(NarrativeSource.Template);
        _runs.Received().AddLedgerEntry(Arg.Is<CostLedgerEntry>(e => e != null && e.Kind == LedgerEntryKind.Actual));
    }

    // ---- Idempotency: a re-entry adopts, neither resubmits nor re-estimates --------------------

    [Fact]
    public async Task An_existing_synthesis_batch_is_adopted_and_polled_rather_than_resubmitted()
    {
        var existing = new Batch(
            _ids.NewId(), RunId, BatchStage.Synthesis, ModelTier.Deep, "msgbatch_fake_0001",
            "digest-narrative-v1", itemCount: 1, Now.AddMinutes(-1));
        _runs.FindBatchAsync(RunId, BatchStage.Synthesis, ModelTier.Deep, Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await CreateSynthesizer().SynthesizeAsync(RunId, LiveDay(), CancellationToken.None);

        result.Source.ShouldBe(NarrativeSource.Model);
        _client.SubmitCallCount.ShouldBe(0);
        _runs.DidNotReceive().AddBatch(Arg.Any<Batch>());
    }

    [Fact]
    public async Task A_re_entry_that_already_committed_the_estimate_does_not_write_a_second_one()
    {
        _runs.HasLedgerEntryAsync(RunId, BatchStage.Synthesis, ModelTier.Deep, LedgerEntryKind.Estimated, Arg.Any<CancellationToken>())
            .Returns(true);

        await CreateSynthesizer().SynthesizeAsync(RunId, LiveDay(), CancellationToken.None);

        _runs.DidNotReceive().AddLedgerEntry(Arg.Is<CostLedgerEntry>(e => e != null && e.Kind == LedgerEntryKind.Estimated));
    }

    [Fact]
    public async Task A_re_entry_that_already_billed_the_actual_does_not_write_a_second_one()
    {
        _runs.HasLedgerEntryAsync(RunId, BatchStage.Synthesis, ModelTier.Deep, LedgerEntryKind.Actual, Arg.Any<CancellationToken>())
            .Returns(true);

        await CreateSynthesizer().SynthesizeAsync(RunId, LiveDay(), CancellationToken.None);

        _runs.DidNotReceive().AddLedgerEntry(Arg.Is<CostLedgerEntry>(e => e != null && e.Kind == LedgerEntryKind.Actual));
    }

    [Fact]
    public async Task A_null_input_is_a_programmer_error_and_throws()
    {
        await Should.ThrowAsync<ArgumentNullException>(() =>
            CreateSynthesizer().SynthesizeAsync(RunId, null!, CancellationToken.None));
    }

    /// <summary>A <see cref="LlmBatchClientException"/> subtype, standing in for a real adapter fault.</summary>
    private sealed class AdapterFault(string message) : LlmBatchClientException(message);

    /// <summary>An <see cref="ILlmBatchClient"/> whose submission throws — a provider or transport fault.</summary>
    private sealed class ThrowingClient(Exception toThrow) : ILlmBatchClient
    {
        public Task<string> SubmitAsync(BatchSubmission submission, CancellationToken cancellationToken) =>
            throw toThrow;

        public Task<BatchStatus> GetStatusAsync(string providerBatchId, CancellationToken cancellationToken) =>
            Task.FromResult(new BatchStatus(ProviderBatchState.Ended, 0, 0, 0));

        public async IAsyncEnumerable<BatchResultItem> GetResultsAsync(
            string providerBatchId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<ProviderBatchRef>> ListRecentBatchesAsync(
            DateTimeOffset createdOnOrAfter, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderBatchRef>>([]);
    }

    /// <summary>Records the order of submission relative to the ledger write (mirrors the matching test).</summary>
    private sealed class RecordingClient(List<string> events) : ILlmBatchClient
    {
        public Task<string> SubmitAsync(BatchSubmission submission, CancellationToken cancellationToken)
        {
            events.Add("submit");
            return Task.FromResult("msgbatch_recording");
        }

        public Task<BatchStatus> GetStatusAsync(string providerBatchId, CancellationToken cancellationToken) =>
            Task.FromResult(new BatchStatus(ProviderBatchState.Ended, 0, 0, 0));

        public async IAsyncEnumerable<BatchResultItem> GetResultsAsync(
            string providerBatchId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<ProviderBatchRef>> ListRecentBatchesAsync(
            DateTimeOffset createdOnOrAfter, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderBatchRef>>([]);
    }
}
