using JobHunter.Application.Matching;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Profiles;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;

namespace JobHunter.Application.Tests.Matching;

/// <summary>
/// T06: the matching result-processing step (F4 SAD §6.1). Each item is parsed independently, the valid
/// ones upserted and the bad ones recorded, the actual cost ledgered from reported usage, and the Run
/// advanced to Ranking. The properties that carry the feature: a mixed batch <em>stores valid, records
/// invalid and still completes</em> (AC-10/QG-3); reprocessing the same results <em>writes no duplicate
/// match and no extra ledger entry</em> (invariant 3); every match is <em>stamped with the CV version</em>
/// so re-staling can find it later (AC-08); a match with no reasons is <em>recorded failed, not persisted</em>
/// (AC-02, enforced by the parser). Repository, parser and client are substituted, so these are zero-database
/// unit tests.
/// </summary>
public sealed class MatchingResultProcessingHandlerTests
{
    private static readonly DateTimeOffset SubmittedAt = new(2026, 8, 3, 2, 5, 0, TimeSpan.Zero);
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000B1");
    private static readonly Guid ProfileId = Guid.Parse("00000000-0000-0000-0000-0000000000C1");
    private static readonly Guid CvVersionId = Guid.Parse("00000000-0000-0000-0000-0000000000D1");

    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly FakeMatchRepository _matches = new();
    private readonly IMatchResultParser _parser = Substitute.For<IMatchResultParser>();
    private readonly IProfileRepository _profiles = Substitute.For<IProfileRepository>();
    private readonly ICvVersionRepository _cvVersions = Substitute.For<ICvVersionRepository>();
    private readonly ICostAccountant _accountant = Substitute.For<ICostAccountant>();
    private readonly FakeClock _clock = new(SubmittedAt.AddHours(1));
    private readonly SequentialIdGenerator _ids = new();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private FakeLlmBatchClient _clientInstance = new();

    public MatchingResultProcessingHandlerTests()
    {
        _parser.Parse(Arg.Any<MatchParseRequest>()).Returns(ci =>
        {
            var req = ci.Arg<MatchParseRequest>()!;
            return string.Equals(req.RawJson, "BAD", StringComparison.Ordinal)
                ? MatchParseOutcome.Failure("sentinel bad item")
                : MatchParseOutcome.Success(NewMatch(req));
        });

        _accountant.Actual(Arg.Any<ModelTier>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(ci => new CostEstimate(0.44m, ci.ArgAt<int>(1), ci.ArgAt<int>(2)));

        _profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns(ActiveProfile());
        _cvVersions.FindActiveAsync(ProfileId, Arg.Any<CancellationToken>()).Returns(ActiveCv());
    }

    private static Profile ActiveProfile() =>
        new(ProfileId, isActive: true, "Owner", 120000m, "USD", TimezoneBand.EMEA,
            ["Portugal"], [EmploymentType.FullTime], SubmittedAt);

    private static CvVersion ActiveCv() =>
        new(CvVersionId, ProfileId, 1, true, "cv.pdf", "application/pdf",
            2048, new string('a', 64), "SENTINEL_CV_TEXT — fifteen years of backend engineering.",
            SubmittedAt, SubmittedAt);

    private static Match NewMatch(MatchParseRequest req) =>
        new(req.MatchId, req.JobId, req.RunId, req.ProfileId, req.CvVersionId, 72,
            InterviewProbability.Good, [], salaryExpectation: null, ["a reason"], req.PromptVersion, req.CreatedAt);

    private MatchingResultProcessingHandler CreateHandler() =>
        new(_runs, _matches, _parser, _profiles, _cvVersions, _clientInstance, _accountant, _clock, _ids,
            NullLogger<MatchingResultProcessingHandler>.Instance);

    private static Run MatchingRun()
    {
        var run = new Run(RunId, SubmittedAt.AddHours(-24), SubmittedAt, 2.00m, SubmittedAt.AddMinutes(-5));
        run.SetScope(3);
        run.TransitionTo(RunState.Enriching, SubmittedAt);
        run.TransitionTo(RunState.Matching, SubmittedAt);
        return run;
    }

    private Batch GivenBatch()
    {
        var batch = new Batch(
            Guid.CreateVersion7(), RunId, BatchStage.Matching, ModelTier.Deep,
            "msgbatch_match_01", "match-v1", 3, SubmittedAt);
        batch.TransitionTo(BatchState.InProgress, SubmittedAt.AddMinutes(2));
        _runs.FindBatchAsync(RunId, BatchStage.Matching, ModelTier.Deep, Arg.Any<CancellationToken>())
            .Returns(batch);
        return batch;
    }

    private List<BatchItem> GivenItems(Batch batch, params Guid[] jobIds)
    {
        var items = jobIds
            .Select(jid => new BatchItem(Guid.CreateVersion7(), batch.Id, jid.ToString(), jid))
            .ToList();
        _runs.FindBatchItemsAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(items);
        return items;
    }

    private void GivenResults(params BatchResultItem[] results) =>
        _clientInstance = new FakeLlmBatchClient(results);

    private static BatchResultItem Ok(Guid jobId, int inTok = 2140, int outTok = 400) =>
        new(jobId.ToString(), "{\"ok\":true}", null, new TokenUsage(inTok, outTok));

    private static BatchResultItem Bad(Guid jobId) =>
        new(jobId.ToString(), "BAD", null, new TokenUsage(2000, 40));

    private static BatchResultItem ProviderFailed(Guid jobId) =>
        new(jobId.ToString(), null, "invalid_request_error", new TokenUsage(0, 0));

    private List<object> Publishes() =>
        _bus.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IMessageBus.PublishAsync))
            .Select(c => c.GetArguments())
            .Where(a => a.Length > 0 && a[0] is not null)
            .Select(a => a[0]!)
            .ToList();

    // ---- AC-10 / QG-3: mixed batch stores valid, records invalid, advances the Run --------------

    [Fact]
    public async Task A_mixed_batch_stores_the_valid_items_records_the_bad_ones_and_advances_the_run()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();
        var jobs = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() };
        var items = GivenItems(batch, jobs);
        GivenResults(Ok(jobs[0]), Bad(jobs[1]), ProviderFailed(jobs[2]));

        await CreateHandler().Handle(new MatchingResultsReady(RunId, batch.Id, batch.ProviderBatchId), _bus, CancellationToken.None);

        _matches.Upserted.Count.ShouldBe(1);
        items[0].State.ShouldBe(BatchItemState.Parsed);
        items[1].State.ShouldBe(BatchItemState.ParseFailed);
        items[1].RawResult.ShouldBe("BAD");
        items[2].State.ShouldBe(BatchItemState.ProviderError);
        batch.State.ShouldBe(BatchState.Completed);
        run.State.ShouldBe(RunState.Ranking);

        var completed = Publishes().OfType<MatchingCompleted>().ShouldHaveSingleItem();
        completed.Succeeded.ShouldBe(1);
        completed.Failed.ShouldBe(2);
    }

    // ---- AC-08: every stored match is stamped with the profile and CV version ------------------

    [Fact]
    public async Task Every_stored_match_is_stamped_with_the_active_profile_and_cv_version()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();
        var jobs = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        GivenItems(batch, jobs);
        GivenResults(Ok(jobs[0]), Ok(jobs[1]));

        await CreateHandler().Handle(new MatchingResultsReady(RunId, batch.Id, batch.ProviderBatchId), _bus, CancellationToken.None);

        _matches.Upserted.ShouldAllBe(m => m.ProfileId == ProfileId && m.CvVersionId == CvVersionId);
    }

    // ---- AC-10: actual cost is written from reported usage, per stage and tier -----------------

    [Fact]
    public async Task The_actual_cost_is_ledgered_from_the_summed_reported_usage_at_the_deep_tier()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();
        var jobs = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        GivenItems(batch, jobs);
        GivenResults(Ok(jobs[0], 2140, 400), Ok(jobs[1], 2000, 350));

        CostLedgerEntry? ledgered = null;
        _runs.When(r => r.AddLedgerEntry(Arg.Any<CostLedgerEntry>()))
            .Do(ci => ledgered = ci.Arg<CostLedgerEntry>());

        await CreateHandler().Handle(new MatchingResultsReady(RunId, batch.Id, batch.ProviderBatchId), _bus, CancellationToken.None);

        _accountant.Received(1).Actual(ModelTier.Deep, 4140, 750);
        ledgered.ShouldNotBeNull();
        ledgered!.Kind.ShouldBe(LedgerEntryKind.Actual);
        ledgered.Stage.ShouldBe(BatchStage.Matching);
        ledgered.Tier.ShouldBe(ModelTier.Deep);
        ledgered.BatchId.ShouldBe(batch.Id);
        batch.InputTokens.ShouldBe(4140);
        batch.OutputTokens.ShouldBe(750);

        Publishes().OfType<MatchingCompleted>().ShouldHaveSingleItem().CostUsd.ShouldBe(0.44m);
    }

    // ---- invariant 3 / crash: reprocessing is idempotent ---------------------------------------

    [Fact]
    public async Task Reprocessing_already_parsed_items_writes_no_duplicate_match_and_no_extra_ledger_entry()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();
        var jobs = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        var items = GivenItems(batch, jobs);
        items[0].MarkParsed();
        items[1].MarkParsed();
        _runs.HasLedgerEntryAsync(RunId, BatchStage.Matching, ModelTier.Deep, LedgerEntryKind.Actual, Arg.Any<CancellationToken>())
            .Returns(true);
        GivenResults(Ok(jobs[0]), Ok(jobs[1]));

        await CreateHandler().Handle(new MatchingResultsReady(RunId, batch.Id, batch.ProviderBatchId), _bus, CancellationToken.None);

        _matches.Upserted.ShouldBeEmpty();
        _runs.DidNotReceive().AddLedgerEntry(Arg.Any<CostLedgerEntry>());
        run.State.ShouldBe(RunState.Ranking);
    }

    [Fact]
    public async Task An_already_completed_batch_still_advances_a_run_stuck_before_ranking()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();
        batch.TransitionTo(BatchState.Completed, SubmittedAt.AddMinutes(30), 100, 10);

        await CreateHandler().Handle(new MatchingResultsReady(RunId, batch.Id, batch.ProviderBatchId), _bus, CancellationToken.None);

        run.State.ShouldBe(RunState.Ranking);
        Publishes().OfType<MatchingCompleted>().ShouldHaveSingleItem();
        _accountant.DidNotReceive().Actual(Arg.Any<ModelTier>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public async Task Handling_the_same_results_twice_leaves_one_match_per_job()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();
        var jobs = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        GivenItems(batch, jobs);
        GivenResults(Ok(jobs[0]), Ok(jobs[1]));

        var handler = CreateHandler();
        await handler.Handle(new MatchingResultsReady(RunId, batch.Id, batch.ProviderBatchId), _bus, CancellationToken.None);
        // A redelivery: the items are now Parsed and the batch Completed, so the second pass adds nothing.
        await handler.Handle(new MatchingResultsReady(RunId, batch.Id, batch.ProviderBatchId), _bus, CancellationToken.None);

        _matches.Upserted.Select(m => m.JobId).Distinct().Count().ShouldBe(2);
        _matches.Upserted.Count.ShouldBe(2);
    }

    // ---- AC-02: a match with no reasons is recorded failed, never persisted --------------------

    [Fact]
    public async Task A_match_the_parser_rejects_for_no_reasons_is_recorded_failed_not_persisted()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();
        var jobs = new[] { Guid.CreateVersion7() };
        var items = GivenItems(batch, jobs);
        // "BAD" is the sentinel the substituted parser fails — standing in for a reasonless payload the
        // tolerant parser would reject before a Match is ever constructed (invariant 4).
        GivenResults(Bad(jobs[0]));

        await CreateHandler().Handle(new MatchingResultsReady(RunId, batch.Id, batch.ProviderBatchId), _bus, CancellationToken.None);

        _matches.Upserted.ShouldBeEmpty();
        items[0].State.ShouldBe(BatchItemState.ParseFailed);
        run.State.ShouldBe(RunState.Ranking);
    }

    // ---- no CV / profile at result time: complete to Ranking without stalling ------------------

    [Fact]
    public async Task Results_arriving_with_no_active_cv_complete_to_ranking_without_storing_matches()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();
        GivenItems(batch, Guid.CreateVersion7());
        _cvVersions.FindActiveAsync(ProfileId, Arg.Any<CancellationToken>()).Returns((CvVersion?)null);

        await CreateHandler().Handle(new MatchingResultsReady(RunId, batch.Id, batch.ProviderBatchId), _bus, CancellationToken.None);

        _matches.Upserted.ShouldBeEmpty();
        run.State.ShouldBe(RunState.Ranking);
        batch.State.ShouldBe(BatchState.Completed);
        Publishes().OfType<MatchingCompleted>().ShouldHaveSingleItem();
    }

    // ---- guards --------------------------------------------------------------------------------

    [Fact]
    public async Task An_all_valid_batch_carries_over_nothing()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();
        var jobs = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        GivenItems(batch, jobs);
        GivenResults(Ok(jobs[0]), Ok(jobs[1]));

        await CreateHandler().Handle(new MatchingResultsReady(RunId, batch.Id, batch.ProviderBatchId), _bus, CancellationToken.None);

        run.JobsCarriedOver.ShouldBe(0);
        _matches.Upserted.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_result_for_an_unknown_custom_id_is_skipped_without_throwing()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();
        var jobs = new[] { Guid.CreateVersion7() };
        GivenItems(batch, jobs);
        GivenResults(Ok(jobs[0]), Ok(Guid.CreateVersion7()));

        await CreateHandler().Handle(new MatchingResultsReady(RunId, batch.Id, batch.ProviderBatchId), _bus, CancellationToken.None);

        _matches.Upserted.Count.ShouldBe(1);
        run.State.ShouldBe(RunState.Ranking);
    }

    [Fact]
    public async Task An_unknown_run_is_ignored()
    {
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns((Run?)null);

        await CreateHandler().Handle(new MatchingResultsReady(RunId, Guid.NewGuid(), "x"), _bus, CancellationToken.None);

        _matches.Upserted.ShouldBeEmpty();
        Publishes().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_terminal_run_is_ignored()
    {
        var run = MatchingRun();
        run.Abort("done", SubmittedAt.AddHours(1), costBreach: false);
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);

        await CreateHandler().Handle(new MatchingResultsReady(RunId, Guid.NewGuid(), "x"), _bus, CancellationToken.None);

        _matches.Upserted.ShouldBeEmpty();
        Publishes().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_with_no_batch_is_a_no_op()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        _runs.FindBatchAsync(RunId, BatchStage.Matching, ModelTier.Deep, Arg.Any<CancellationToken>())
            .Returns((Batch?)null);

        await CreateHandler().Handle(new MatchingResultsReady(RunId, Guid.NewGuid(), "x"), _bus, CancellationToken.None);

        _matches.Upserted.ShouldBeEmpty();
        Publishes().ShouldBeEmpty();
    }

    private sealed class FakeMatchRepository : IMatchRepository
    {
        public List<Match> Upserted { get; } = [];

        public Task<bool> UpsertAsync(Match match, CancellationToken cancellationToken = default)
        {
            // Model the unique (job_id, run_id, profile_id) index: a duplicate is an idempotent no-op.
            var isNew = !Upserted.Any(m => m.JobId == match.JobId && m.RunId == match.RunId && m.ProfileId == match.ProfileId);
            if (isNew)
            {
                Upserted.Add(match);
            }

            return Task.FromResult(isNew);
        }

        public Task<Match?> FindAsync(Guid jobId, Guid runId, Guid profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Upserted.FirstOrDefault(m => m.JobId == jobId && m.RunId == runId && m.ProfileId == profileId));

        public Task<int> MarkNotCurrentForCvVersionAsync(Guid cvVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}
