using JobHunter.Application.Enrichment;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;

namespace JobHunter.Application.Tests.Enrichment;

/// <summary>
/// T10: the one spend-committing step, and the two invariants it exists to enforce (F3 SAD §6.2,
/// ADR-F3-0002). QG-2 is asserted as an <em>absence</em>: a breaching estimate must never reach the
/// client, proven with <see cref="FakeLlmBatchClient.ThrowOnSubmit"/> (AC-03). AC-04 is asserted as an
/// ordering: the estimate ledger entry is committed before <see cref="ILlmBatchClient.SubmitAsync"/> is
/// called in every path. The repository, scope query and request builder are substituted, so these are
/// zero-database unit tests; the crash-matrix (T13) proves the same properties end-to-end against Postgres.
/// </summary>
public sealed class EnrichmentSubmitHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 2, 5, 0, TimeSpan.Zero);
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");

    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly IEnrichmentScopeQuery _scope = Substitute.For<IEnrichmentScopeQuery>();
    private readonly IEnrichmentRequestBuilder _builder = Substitute.For<IEnrichmentRequestBuilder>();
    private readonly ICostAccountant _accountant = Substitute.For<ICostAccountant>();
    private readonly FakeLlmBatchClient _client = new();
    private readonly FakeClock _clock = new(Now);
    private readonly SequentialIdGenerator _ids = new();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private EnrichmentSubmitHandler CreateHandler() =>
        new(_runs, _scope, _builder, _accountant, _client, _clock, _ids, NullLogger<EnrichmentSubmitHandler>.Instance);

    private List<object> Published() =>
        _bus.ReceivedCalls()
            .Select(c => c.GetArguments())
            .Where(a => a.Length > 0 && a[0] is not null)
            .Select(a => a[0]!)
            .ToList();

    private static Run CreatedRun(decimal ceiling = 2.00m, decimal spent = 0m)
    {
        var run = new Run(RunId, Now.AddHours(-24), Now, ceiling, Now.AddHours(-3));
        run.SetScope(2);
        if (spent > 0m)
        {
            run.SetSpend(spent);
        }

        return run;
    }

    private static EnrichmentJobContent Job(string title = "Backend Engineer") =>
        new(Guid.CreateVersion7(), "Acme", "acme.com", title, "Remote", null, "FullTime", "We build things.");

    private void GivenScope(params EnrichmentJobContent[] jobs)
    {
        _scope.InScopeAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(jobs.ToList());

        var items = jobs
            .Select(j => new BatchRequestItem(j.JobId.ToString(), "system", $"content-{j.Title}",
                new JsonSchema("record_enrichment", "{}")))
            .ToList();
        _builder.Build(Arg.Any<IReadOnlyList<EnrichmentJobContent>>())
            .Returns(new EnrichmentBatchRequest("enrich-v1", items, 350));

        _runs.FindRetriableJobIdsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());
    }

    private void GivenEstimate(decimal costUsd, int inputTokens = 1000, int outputTokens = 700) =>
        _accountant.Estimate(Arg.Any<ModelTier>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>())
            .Returns(new CostEstimate(costUsd, inputTokens, outputTokens));

    // ---- QG-2: the ceiling is a precondition, the client is never called on a breach ----------

    [Fact]
    public async Task Estimate_exceeding_ceiling_never_calls_the_client_and_aborts_the_run()
    {
        var run = CreatedRun(ceiling: 0.10m);
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        _runs.FindBatchAsync(RunId, BatchStage.Enrichment, ModelTier.Cheap, Arg.Any<CancellationToken>())
            .Returns((Batch?)null);
        GivenScope(Job(), Job("Platform Engineer"));
        GivenEstimate(0.43m);

        // The tripwire: the test passes only if SubmitAsync is never reached (AC-03, QG-2).
        _client.ThrowOnSubmit = true;

        await CreateHandler().Handle(new EnrichmentSubmissionDue(RunId), _bus, CancellationToken.None);

        _client.SubmitCallCount.ShouldBe(0);
        run.State.ShouldBe(RunState.CostAborted);
        run.FailureReason.ShouldNotBeNullOrWhiteSpace();
        Published().OfType<RunCostAborted>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Cost_abort_records_no_ledger_estimate_and_no_batch()
    {
        var run = CreatedRun(ceiling: 0.10m);
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(Job());
        GivenEstimate(0.43m);
        _client.ThrowOnSubmit = true;

        await CreateHandler().Handle(new EnrichmentSubmissionDue(RunId), _bus, CancellationToken.None);

        _runs.DidNotReceive().AddLedgerEntry(Arg.Any<CostLedgerEntry>());
        _runs.DidNotReceive().AddBatch(Arg.Any<Batch>());
    }

    [Fact]
    public async Task Spend_already_incurred_counts_toward_the_ceiling()
    {
        // Estimate alone is within ceiling, but prior spend pushes the projection over — still aborts.
        var run = CreatedRun(ceiling: 0.50m, spent: 0.40m);
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(Job());
        GivenEstimate(0.20m);
        _client.ThrowOnSubmit = true;

        await CreateHandler().Handle(new EnrichmentSubmissionDue(RunId), _bus, CancellationToken.None);

        _client.SubmitCallCount.ShouldBe(0);
        run.State.ShouldBe(RunState.CostAborted);
    }

    // ---- AC-04: the estimate is ledgered and committed BEFORE the client is called ------------

    [Fact]
    public async Task Within_ceiling_ledgers_the_estimate_before_calling_the_client()
    {
        var run = CreatedRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(Job(), Job("Platform Engineer"));
        GivenEstimate(0.43m);

        var events = new List<string>();
        _runs.When(r => r.AddLedgerEntry(Arg.Is<CostLedgerEntry>(e => e != null && e.Kind == LedgerEntryKind.Estimated)))
            .Do(_ => events.Add("ledger"));
        _runs.When(r => r.SaveChangesAsync(Arg.Any<CancellationToken>())).Do(_ => events.Add("save"));

        // Record the moment of the client call relative to the ledger writes.
        var submittingClient = new RecordingClient(events);
        var handler = new EnrichmentSubmitHandler(
            _runs, _scope, _builder, _accountant, submittingClient, _clock, _ids,
            NullLogger<EnrichmentSubmitHandler>.Instance);

        await handler.Handle(new EnrichmentSubmissionDue(RunId), _bus, CancellationToken.None);

        // The estimate is added and committed (save) before submit — the ordering ADR-F3-0002 mandates.
        var ledgerIndex = events.IndexOf("ledger");
        var firstSaveAfterLedger = events.FindIndex(ledgerIndex, e => e == "save");
        var submitIndex = events.IndexOf("submit");
        ledgerIndex.ShouldBeGreaterThanOrEqualTo(0);
        submitIndex.ShouldBeGreaterThan(firstSaveAfterLedger);
    }

    [Fact]
    public async Task Within_ceiling_submits_persists_the_batch_and_moves_to_enriching()
    {
        var run = CreatedRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(Job(), Job("Platform Engineer"));
        GivenEstimate(0.43m);
        _client.ProviderBatchId = "msgbatch_live_01";

        Batch? persisted = null;
        _runs.When(r => r.AddBatch(Arg.Any<Batch>())).Do(ci => persisted = ci.Arg<Batch>());

        await CreateHandler().Handle(new EnrichmentSubmissionDue(RunId), _bus, CancellationToken.None);

        _client.SubmitCallCount.ShouldBe(1);
        var submission = _client.LastSubmission.ShouldNotBeNull();
        submission.Tier.ShouldBe(ModelTier.Cheap);
        submission.PromptVersion.ShouldBe("enrich-v1");
        submission.Items.Count.ShouldBe(2);

        var batch = persisted.ShouldNotBeNull();
        batch.ProviderBatchId.ShouldBe("msgbatch_live_01");
        batch.ItemCount.ShouldBe(2);
        run.State.ShouldBe(RunState.Enriching);
    }

    [Fact]
    public async Task Within_ceiling_persists_one_batch_item_per_job_with_the_job_id_as_custom_id()
    {
        var run = CreatedRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var a = Job();
        var b = Job("Platform Engineer");
        GivenScope(a, b);
        GivenEstimate(0.43m);

        var items = new List<BatchItem>();
        _runs.When(r => r.AddBatchItem(Arg.Any<BatchItem>())).Do(ci => items.Add((BatchItem)ci[0]!));

        await CreateHandler().Handle(new EnrichmentSubmissionDue(RunId), _bus, CancellationToken.None);

        items.Count.ShouldBe(2);
        items.Select(i => i.CustomId).ShouldBe([a.JobId.ToString(), b.JobId.ToString()], ignoreOrder: true);
        items.Select(i => i.JobId).ShouldBe([a.JobId, b.JobId], ignoreOrder: true);
    }

    [Fact]
    public async Task Within_ceiling_publishes_submitted_and_poll_due()
    {
        var run = CreatedRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(Job());
        GivenEstimate(0.43m);

        await CreateHandler().Handle(new EnrichmentSubmissionDue(RunId), _bus, CancellationToken.None);

        Published().OfType<EnrichmentBatchSubmitted>().ShouldHaveSingleItem();
        Published().OfType<BatchPollDue>().ShouldHaveSingleItem();
    }

    // ---- AC-08: the previous Run's failed items retry once --------------------------------------

    [Fact]
    public async Task Carried_over_failed_items_are_included_in_the_scope_query()
    {
        var run = CreatedRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var carried = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        GivenScope(Job());
        GivenEstimate(0.43m);
        // Set after GivenScope, which stubs FindRetriableJobIdsAsync to empty by default.
        _runs.FindRetriableJobIdsAsync(Arg.Any<CancellationToken>()).Returns(carried);

        await CreateHandler().Handle(new EnrichmentSubmissionDue(RunId), _bus, CancellationToken.None);

        await _scope.Received(1).InScopeAsync(
            run.CutoffFrom, run.CutoffTo,
            Arg.Is<IReadOnlyCollection<Guid>>(c => c != null && c.Count == 2 && c.Contains(carried[0]) && c.Contains(carried[1])),
            Arg.Any<CancellationToken>());
    }

    // ---- Idempotency (QG-1): a redelivery never resubmits --------------------------------------

    [Fact]
    public async Task An_already_submitted_batch_polls_rather_than_resubmitting()
    {
        var run = CreatedRun();
        run.TransitionTo(RunState.Enriching, Now);
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var existing = new Batch(
            Guid.CreateVersion7(), RunId, BatchStage.Enrichment, ModelTier.Cheap,
            "msgbatch_existing", "enrich-v1", 2, Now);
        _runs.FindBatchAsync(RunId, BatchStage.Enrichment, ModelTier.Cheap, Arg.Any<CancellationToken>())
            .Returns(existing);
        _client.ThrowOnSubmit = true;

        await CreateHandler().Handle(new EnrichmentSubmissionDue(RunId), _bus, CancellationToken.None);

        _client.SubmitCallCount.ShouldBe(0);
        Published().OfType<BatchPollDue>().ShouldHaveSingleItem();
        _runs.DidNotReceive().AddBatch(Arg.Any<Batch>());
    }

    [Fact]
    public async Task A_committed_estimate_from_a_prior_attempt_is_not_written_twice()
    {
        var run = CreatedRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(Job());
        GivenEstimate(0.43m);
        // The estimate was already committed before the crash; the resume must not double-count it.
        _runs.HasLedgerEntryAsync(RunId, BatchStage.Enrichment, ModelTier.Cheap, LedgerEntryKind.Estimated,
            Arg.Any<CancellationToken>()).Returns(true);

        await CreateHandler().Handle(new EnrichmentSubmissionDue(RunId), _bus, CancellationToken.None);

        _runs.DidNotReceive().AddLedgerEntry(Arg.Any<CostLedgerEntry>());
        _client.SubmitCallCount.ShouldBe(1);
    }

    // ---- Edge cases -----------------------------------------------------------------------------

    [Fact]
    public async Task Empty_scope_at_submission_completes_to_matching_without_spending()
    {
        var run = CreatedRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(); // nothing in scope now
        _client.ThrowOnSubmit = true;

        await CreateHandler().Handle(new EnrichmentSubmissionDue(RunId), _bus, CancellationToken.None);

        _client.SubmitCallCount.ShouldBe(0);
        run.State.ShouldBe(RunState.Matching);
        run.JobsInScope.ShouldBe(0);
        Published().OfType<EnrichmentCompleted>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Unknown_run_is_ignored()
    {
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns((Run?)null);
        _client.ThrowOnSubmit = true;

        await CreateHandler().Handle(new EnrichmentSubmissionDue(RunId), _bus, CancellationToken.None);

        _client.SubmitCallCount.ShouldBe(0);
        Published().ShouldBeEmpty();
    }

    [Fact]
    public async Task Terminal_run_is_ignored()
    {
        var run = CreatedRun();
        run.Abort("already done", Now, costBreach: false);
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        _client.ThrowOnSubmit = true;

        await CreateHandler().Handle(new EnrichmentSubmissionDue(RunId), _bus, CancellationToken.None);

        _client.SubmitCallCount.ShouldBe(0);
    }

    /// <summary>A client that records the instant of its submit call into a shared ordering log (AC-04).</summary>
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
    }
}
