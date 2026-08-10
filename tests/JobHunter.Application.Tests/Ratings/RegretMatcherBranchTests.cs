using System.Runtime.CompilerServices;
using JobHunter.Application.Ratings;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Profiles;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Ratings;

/// <summary>
/// F4 T21 (ADR-F4-0003): the non-happy arms of the regret matcher — the ones that prove its central promise,
/// that <em>whatever goes wrong it returns what it has and never throws</em>. A budget that elapses, a provider
/// fault, a transport fault, a provider-side cancelled or expired batch, a per-item provider error, and an
/// unparseable custom_id are each exercised here: the first four collapse the whole sample to no regrets, and
/// the last two drop only the one offending item. These complement the happy-path facts in
/// <see cref="RegretMatcherTests"/>. Every collaborator is a fake or a substitute, so these stay zero-network.
/// </summary>
public sealed class RegretMatcherBranchTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeClock _clock = new(Now);
    private readonly SequentialIdGenerator _ids = new();
    private readonly IProfileRepository _profiles = Substitute.For<IProfileRepository>();
    private readonly ICvVersionRepository _cvVersions = Substitute.For<ICvVersionRepository>();
    private readonly FakeMatchRequestBuilder _builder = new();
    private readonly FakeMatchResultParser _parser = new();

    [Fact]
    public async Task A_batch_that_does_not_end_within_the_budget_returns_no_regrets()
    {
        // The client stalls past the budget: the linked token cancels the poll, the OperationCanceledException
        // is caught, and a weekly diagnostic answers with no regrets rather than hanging the job.
        WithActiveCv();
        var slow = new FakeLlmBatchClient(pollsBeforeEnd: 1000) { Delay = TimeSpan.FromMilliseconds(200) };
        var tight = new RegretMatchingOptions
        {
            Timeout = TimeSpan.FromMilliseconds(20),
            PollInterval = TimeSpan.FromMilliseconds(5),
        };
        var matcher = NewMatcher(slow, tight);

        var results = await matcher.MatchAsync([Content(JobId(1))]);

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_caller_cancellation_returns_no_regrets_rather_than_throwing()
    {
        WithActiveCv();
        var slow = new FakeLlmBatchClient(pollsBeforeEnd: 1000) { Delay = TimeSpan.FromMilliseconds(200) };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var results = await NewMatcher(slow).MatchAsync([Content(JobId(1))], cts.Token);

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_provider_fault_returns_no_regrets_rather_than_throwing()
    {
        WithActiveCv();
        var faulting = new ThrowingClient(new AdapterFault("the provider returned 503"));

        var results = await NewMatcher(faulting).MatchAsync([Content(JobId(1))]);

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_transport_fault_returns_no_regrets_rather_than_throwing()
    {
        WithActiveCv();
        var faulting = new ThrowingClient(new HttpRequestException("connection reset"));

        var results = await NewMatcher(faulting).MatchAsync([Content(JobId(1))]);

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_provider_side_cancelled_batch_returns_no_regrets()
    {
        WithActiveCv();
        var cancelled = new FakeLlmBatchClient(
            results: [Result(JobId(1))], terminalState: ProviderBatchState.Cancelled);

        var results = await NewMatcher(cancelled).MatchAsync([Content(JobId(1))]);

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_provider_side_expired_batch_returns_no_regrets()
    {
        WithActiveCv();
        var expired = new FakeLlmBatchClient(
            results: [Result(JobId(1))], terminalState: ProviderBatchState.Expired);

        var results = await NewMatcher(expired).MatchAsync([Content(JobId(1))]);

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_item_that_errored_at_the_provider_is_dropped_and_the_rest_are_scored()
    {
        var good = JobId(1);
        var errored = JobId(2);
        _parser.Scores[good] = 64;
        WithActiveCv();
        var client = new FakeLlmBatchClient(
        [
            Result(good),
            new BatchResultItem(errored.ToString(), null, "overloaded_error", TokenUsage.Zero),
        ]);

        var results = await NewMatcher(client).MatchAsync([Content(good), Content(errored)]);

        // The provider-side error drops only its own item; the good one still scores.
        results.Count.ShouldBe(1);
        results[0].JobId.ShouldBe(good);
    }

    [Fact]
    public async Task A_result_with_an_unparseable_custom_id_is_dropped_and_the_rest_are_scored()
    {
        var good = JobId(1);
        _parser.Scores[good] = 71;
        WithActiveCv();
        var client = new FakeLlmBatchClient(
        [
            Result(good),
            new BatchResultItem("not-a-guid", "{\"matchScore\":0}", null, TokenUsage.Zero),
        ]);

        var results = await NewMatcher(client).MatchAsync([Content(good), Content(JobId(2))]);

        // The custom_id must round-trip to a job id or the item is dropped — one bad envelope is not the sample.
        results.Count.ShouldBe(1);
        results[0].JobId.ShouldBe(good);
    }

    private RegretMatcher NewMatcher(ILlmBatchClient client, RegretMatchingOptions? options = null) =>
        new(_profiles, _cvVersions, _builder, _parser, client, _clock, _ids,
            options ?? new RegretMatchingOptions(), NullLogger<RegretMatcher>.Instance);

    private Profile WithActiveCv()
    {
        var profile = ProfileFixture();
        _profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns(profile);
        _cvVersions.FindActiveAsync(profile.Id, Arg.Any<CancellationToken>()).Returns(CvFixture(profile.Id));
        return profile;
    }

    private Profile ProfileFixture() =>
        new(_ids.NewId(), isActive: true, "Owner", 100000m, "USD", TimezoneBand.EMEA, [], [], Now);

    private CvVersion CvFixture(Guid profileId) =>
        new(_ids.NewId(), profileId, version: 1, isActive: true, "cv.pdf", "application/pdf", 1024,
            ContentHash.Compute("cv").Value, "CV text", Now, Now);

    private static Guid JobId(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    private static BatchResultItem Result(Guid jobId) =>
        new(jobId.ToString(), "{\"matchScore\":0}", null, TokenUsage.Zero);

    private static MatchJobContent Content(Guid jobId) =>
        new(jobId, "Acme", "acme.com", "Staff SRE", "Staff", "Remote", null, "FullTime", "desc", Enrichment: null);

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
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<ProviderBatchRef>> ListRecentBatchesAsync(
            DateTimeOffset createdOnOrAfter, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderBatchRef>>([]);
    }

    /// <summary>Records the CV boundary it was handed and emits one item per job (custom_id = job id).</summary>
    private sealed class FakeMatchRequestBuilder : IMatchRequestBuilder
    {
        public MatchBatchRequest Build(IReadOnlyList<MatchJobContent> jobs, Profile profile, CvVersion cvVersion)
        {
            var schema = new JsonSchema("record_match", "{}");
            var items = jobs
                .Select(j => new BatchRequestItem(j.JobId.ToString(), "system", "role", schema))
                .ToList();
            return new MatchBatchRequest("match-v1", items, 550);
        }
    }

    /// <summary>Scores by job id and fails any item it has no score for (mirrors a real parse failure).</summary>
    private sealed class FakeMatchResultParser : IMatchResultParser
    {
        public Dictionary<Guid, int> Scores { get; } = new();

        public MatchParseOutcome Parse(MatchParseRequest request)
        {
            if (!Scores.TryGetValue(request.JobId, out var score))
            {
                return MatchParseOutcome.Failure("no score");
            }

            var match = new Match(
                request.MatchId, request.JobId, request.RunId, request.ProfileId, request.CvVersionId,
                score, InterviewProbability.Moderate, [], null, ["fit"], request.PromptVersion, request.CreatedAt);
            return MatchParseOutcome.Success(match);
        }
    }
}
