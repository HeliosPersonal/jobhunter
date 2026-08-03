using JobHunter.Application.Enrichment;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Pipeline;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;
using EnrichmentAggregate = JobHunter.Domain.Intelligence.Enrichment;

namespace JobHunter.Application.Tests.Enrichment;

/// <summary>
/// T12: the result-processing step (F3 SAD §6.2). Each item is parsed independently, the valid ones are
/// upserted and the bad ones recorded, the actual cost is ledgered from reported usage, and the Run
/// advances to Matching. The properties that carry the feature: a mixed batch <em>stores valid, records
/// invalid and still completes</em> (AC-07/QG-3); reprocessing the same results <em>writes no duplicate
/// enrichment and no extra ledger entry</em> (AC-06, crash-matrix checkpoints 7-8); the actual cost is
/// <em>attributable per stage and tier</em> (AC-10). Repository, parser and client are substituted, so
/// these are zero-database unit tests.
/// </summary>
public sealed class BatchResultProcessingHandlerTests
{
    private static readonly DateTimeOffset SubmittedAt = new(2026, 8, 3, 2, 5, 0, TimeSpan.Zero);
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000B1");

    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly FakeEnrichmentRepository _enrichments = new();
    private readonly IEnrichmentResultParser _parser = Substitute.For<IEnrichmentResultParser>();
    private readonly ICostAccountant _accountant = Substitute.For<ICostAccountant>();
    private readonly FakeClock _clock = new(SubmittedAt.AddHours(1));
    private readonly SequentialIdGenerator _ids = new();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    public BatchResultProcessingHandlerTests()
    {
        // The parser succeeds for any payload that is not the sentinel "BAD"; a real parser's tolerance is
        // exercised in EnrichmentResultParserTests. Failures here are driven by the sentinel so the handler's
        // per-item isolation is what is under test, not the parsing rules.
        _parser.Parse(Arg.Any<EnrichmentParseRequest>()).Returns(ci =>
        {
            var req = ci.Arg<EnrichmentParseRequest>()!;
            var raw = req.RawJson;
            return string.Equals(raw, "BAD", StringComparison.Ordinal)
                ? EnrichmentParseOutcome.Failure("sentinel bad item")
                : EnrichmentParseOutcome.Success(NewEnrichment(req));
        });

        _accountant.Actual(Arg.Any<ModelTier>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(ci => new CostEstimate(0.05m, ci.ArgAt<int>(1), ci.ArgAt<int>(2)));
    }

    private static EnrichmentAggregate NewEnrichment(EnrichmentParseRequest req) =>
        new(req.EnrichmentId, req.JobId, req.RunId, salary: null, isRemote: true, isContractorFriendly: false,
            TimezoneBand.EMEA, AiUsageLevel.Low, CompanyStage.Seed, RoleFamily.BackendGeneric, [], ["a reason"],
            req.PromptVersion, req.CreatedAt);

    private BatchResultProcessingHandler CreateHandler() =>
        new(_runs, _enrichments, _parser, _client(), _accountant, _clock, _ids,
            NullLogger<BatchResultProcessingHandler>.Instance);

    private FakeLlmBatchClient _clientInstance = new();
    private FakeLlmBatchClient _client() => _clientInstance;

    private static Run EnrichingRun()
    {
        var run = new Run(RunId, SubmittedAt.AddHours(-24), SubmittedAt, 2.00m, SubmittedAt.AddMinutes(-5));
        run.SetScope(3);
        run.TransitionTo(RunState.Enriching, SubmittedAt);
        return run;
    }

    private Batch GivenBatch()
    {
        var batch = new Batch(
            Guid.CreateVersion7(), RunId, BatchStage.Enrichment, ModelTier.Cheap,
            "msgbatch_result_01", "enrich-v1", 3, SubmittedAt);
        batch.TransitionTo(BatchState.InProgress, SubmittedAt.AddMinutes(2));
        _runs.FindBatchAsync(RunId, BatchStage.Enrichment, ModelTier.Cheap, Arg.Any<CancellationToken>())
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

    private static BatchResultItem Ok(Guid jobId, int inTok = 4000, int outTok = 300) =>
        new(jobId.ToString(), "{\"ok\":true}", null, new TokenUsage(inTok, outTok));

    private static BatchResultItem Bad(Guid jobId) =>
        new(jobId.ToString(), "BAD", null, new TokenUsage(3800, 40));

    private static BatchResultItem ProviderFailed(Guid jobId) =>
        new(jobId.ToString(), null, "invalid_request_error", new TokenUsage(0, 0));

    private List<object> Publishes() =>
        _bus.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IMessageBus.PublishAsync))
            .Select(c => c.GetArguments())
            .Where(a => a.Length > 0 && a[0] is not null)
            .Select(a => a[0]!)
            .ToList();

    // ---- AC-07 / QG-3: mixed batch stores valid, records invalid, completes the Run ------------

    [Fact]
    public async Task A_mixed_batch_stores_the_valid_items_records_the_bad_ones_and_advances_the_run()
    {
        var run = EnrichingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();
        var jobs = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() };
        var items = GivenItems(batch, jobs);
        GivenResults(Ok(jobs[0]), Bad(jobs[1]), ProviderFailed(jobs[2]));

        await CreateHandler().Handle(new BatchResultsReady(RunId, batch.Id, batch.ProviderBatchId), _bus, CancellationToken.None);

        _enrichments.Upserted.Count.ShouldBe(1);
        items[0].State.ShouldBe(BatchItemState.Parsed);
        items[1].State.ShouldBe(BatchItemState.ParseFailed);
        items[1].RawResult.ShouldBe("BAD");
        items[2].State.ShouldBe(BatchItemState.ProviderError);
        batch.State.ShouldBe(BatchState.Completed);
        run.State.ShouldBe(RunState.Matching);

        var completed = Publishes().OfType<EnrichmentCompleted>().ShouldHaveSingleItem();
        completed.EnrichedCount.ShouldBe(1);
        completed.FailedCount.ShouldBe(2);
    }

    // ---- AC-10: actual cost is written from reported usage, per stage and tier -----------------

    [Fact]
    public async Task The_actual_cost_is_ledgered_from_the_summed_reported_usage()
    {
        var run = EnrichingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();
        var jobs = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        GivenItems(batch, jobs);
        GivenResults(Ok(jobs[0], 4000, 300), Ok(jobs[1], 3500, 250));

        CostLedgerEntry? ledgered = null;
        _runs.When(r => r.AddLedgerEntry(Arg.Any<CostLedgerEntry>()))
            .Do(ci => ledgered = ci.Arg<CostLedgerEntry>());

        await CreateHandler().Handle(new BatchResultsReady(RunId, batch.Id, batch.ProviderBatchId), _bus, CancellationToken.None);

        _accountant.Received(1).Actual(ModelTier.Cheap, 7500, 550);
        ledgered.ShouldNotBeNull();
        ledgered!.Kind.ShouldBe(LedgerEntryKind.Actual);
        ledgered.Stage.ShouldBe(BatchStage.Enrichment);
        ledgered.Tier.ShouldBe(ModelTier.Cheap);
        ledgered.BatchId.ShouldBe(batch.Id);
        ledgered.InputTokens.ShouldBe(7500);
        ledgered.OutputTokens.ShouldBe(550);
        batch.InputTokens.ShouldBe(7500);
        batch.OutputTokens.ShouldBe(550);
    }

    // ---- AC-06 / checkpoints 7-8: reprocessing is idempotent -----------------------------------

    [Fact]
    public async Task Reprocessing_already_parsed_items_writes_no_duplicate_enrichment_and_no_extra_ledger_entry()
    {
        var run = EnrichingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();
        var jobs = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        var items = GivenItems(batch, jobs);
        // Simulate the earlier pass having stored both items before the crash.
        items[0].MarkParsed();
        items[1].MarkParsed();
        // The Actual ledger entry was also already written on the earlier pass.
        _runs.HasLedgerEntryAsync(RunId, BatchStage.Enrichment, ModelTier.Cheap, LedgerEntryKind.Actual, Arg.Any<CancellationToken>())
            .Returns(true);
        GivenResults(Ok(jobs[0]), Ok(jobs[1]));

        await CreateHandler().Handle(new BatchResultsReady(RunId, batch.Id, batch.ProviderBatchId), _bus, CancellationToken.None);

        _enrichments.Upserted.ShouldBeEmpty();       // no re-upsert of already-parsed items
        _runs.DidNotReceive().AddLedgerEntry(Arg.Any<CostLedgerEntry>()); // no extra ledger entry
        run.State.ShouldBe(RunState.Matching);        // the Run still advances (checkpoint 8)
    }

    [Fact]
    public async Task An_already_completed_batch_still_advances_a_run_stuck_before_matching()
    {
        // Checkpoint 8: the crash fell between the batch commit and the Run move.
        var run = EnrichingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();
        batch.TransitionTo(BatchState.Completed, SubmittedAt.AddMinutes(30), 100, 10);

        await CreateHandler().Handle(new BatchResultsReady(RunId, batch.Id, batch.ProviderBatchId), _bus, CancellationToken.None);

        run.State.ShouldBe(RunState.Matching);
        Publishes().OfType<EnrichmentCompleted>().ShouldHaveSingleItem();
        _accountant.DidNotReceive().Actual(Arg.Any<ModelTier>(), Arg.Any<int>(), Arg.Any<int>());
    }

    // ---- guards --------------------------------------------------------------------------------

    [Fact]
    public async Task An_all_valid_batch_carries_over_nothing()
    {
        var run = EnrichingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();
        var jobs = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        GivenItems(batch, jobs);
        GivenResults(Ok(jobs[0]), Ok(jobs[1]));

        await CreateHandler().Handle(new BatchResultsReady(RunId, batch.Id, batch.ProviderBatchId), _bus, CancellationToken.None);

        run.JobsCarriedOver.ShouldBe(0);
        _enrichments.Upserted.Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_unknown_run_is_ignored()
    {
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns((Run?)null);

        await CreateHandler().Handle(new BatchResultsReady(RunId, Guid.NewGuid(), "x"), _bus, CancellationToken.None);

        _enrichments.Upserted.ShouldBeEmpty();
        Publishes().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_terminal_run_is_ignored()
    {
        var run = EnrichingRun();
        run.Abort("done", SubmittedAt.AddHours(1), costBreach: false);
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);

        await CreateHandler().Handle(new BatchResultsReady(RunId, Guid.NewGuid(), "x"), _bus, CancellationToken.None);

        _enrichments.Upserted.ShouldBeEmpty();
        Publishes().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_with_no_batch_is_a_no_op()
    {
        var run = EnrichingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        _runs.FindBatchAsync(RunId, BatchStage.Enrichment, ModelTier.Cheap, Arg.Any<CancellationToken>())
            .Returns((Batch?)null);

        await CreateHandler().Handle(new BatchResultsReady(RunId, Guid.NewGuid(), "x"), _bus, CancellationToken.None);

        _enrichments.Upserted.ShouldBeEmpty();
        Publishes().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_result_for_an_unknown_custom_id_is_skipped_without_throwing()
    {
        var run = EnrichingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();
        var jobs = new[] { Guid.CreateVersion7() };
        GivenItems(batch, jobs);
        // A result whose custom_id is not among the batch's items.
        GivenResults(Ok(jobs[0]), Ok(Guid.CreateVersion7()));

        await CreateHandler().Handle(new BatchResultsReady(RunId, batch.Id, batch.ProviderBatchId), _bus, CancellationToken.None);

        _enrichments.Upserted.Count.ShouldBe(1);
        run.State.ShouldBe(RunState.Matching);
    }

    private sealed class FakeEnrichmentRepository : IEnrichmentRepository
    {
        public List<EnrichmentAggregate> Upserted { get; } = [];

        public Task<bool> UpsertAsync(EnrichmentAggregate enrichment, CancellationToken cancellationToken = default)
        {
            Upserted.Add(enrichment);
            return Task.FromResult(true);
        }

        public Task<EnrichmentAggregate?> FindAsync(Guid jobId, Guid runId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EnrichmentAggregate?>(Upserted.FirstOrDefault(e => e.JobId == jobId && e.RunId == runId));
    }
}
