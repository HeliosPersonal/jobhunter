using JobHunter.Application.Enrichment;
using JobHunter.Claude;
using JobHunter.Claude.Enrichment;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;
using EnrichmentAggregate = JobHunter.Domain.Intelligence.Enrichment;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F3's centre of gravity: the eight-checkpoint <strong>crash matrix</strong> (testing-strategy §F3,
/// test-plan §The crash matrix, QG-1). The Run is a five-hour, resumable, cost-bounded process; a worker
/// can die at any point and must resume without paying twice or losing a result. Each checkpoint
/// reconstructs the exact durable state a crash would leave behind at one point in the
/// submit → poll → process flow, then runs the <em>real</em> resume path against a real Postgres database
/// with a counting fake provider client — a fresh repository/context per step, which is how a restarted
/// worker re-reads the last committed state.
///
/// <para><strong>Every checkpoint asserts the same two properties</strong> (<see cref="AssertPaidOnceAndAdvancedAsync"/>):
/// the provider was paid exactly once (<c>SubmitCallCount == 1</c>) and the <c>Actual</c> ledger total
/// after the interrupted-and-resumed Run equals that of an uninterrupted one (<see cref="ExpectedActualUsd"/>,
/// no double-charge). Checkpoint 4 — a crash in the one-statement window between
/// <see cref="ILlmBatchClient.SubmitAsync"/> returning and the batch row committing — is the one that
/// matters most: it is closed by the D5 reconciliation, which adopts the provider's orphaned batch rather
/// than resubmitting. Requires Docker.</para>
/// </summary>
public sealed class CrashMatrixTests
{
    // Well before the 06:45 delivery deadline, so nothing carries over prematurely.
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 2, 0, 0, TimeSpan.Zero);

    private const BatchStage Stage = BatchStage.Enrichment;
    private const ModelTier Tier = ModelTier.Cheap;

    // Deterministic Cheap-tier pricing (mirrors CostAccountantTests): input $1.00/M, output $5.00/M, 50% off.
    private static readonly PricingOptions Pricing = new()
    {
        Tiers = new Dictionary<string, TierPricing>
        {
            ["Cheap"] = new() { ModelId = "claude-haiku-4-5", InputPerMillion = 1.00m, OutputPerMillion = 5.00m, BatchDiscount = 0.5m },
            ["Deep"] = new() { ModelId = "claude-sonnet-5", InputPerMillion = 3.00m, OutputPerMillion = 15.00m, BatchDiscount = 0.5m },
        },
    };

    // Three jobs, each reported at 4000 input / 400 output tokens. The actual cost is priced from these
    // reported figures (not the estimate): (12000·1.00 + 1200·5.00)/1e6 · 0.5 = 0.006 + 0.003 = 0.0090,
    // which stores exactly in the ledger's numeric(8,4) column so the equality assertion is exact.
    private const int JobCount = 3;
    private const int PerItemInputTokens = 4000;
    private const int PerItemOutputTokens = 400;
    private const decimal ExpectedActualUsd = 0.0090m;

    // A valid tool-use payload the real EnrichmentResultParser accepts — two non-empty reasons (invariant 4).
    private const string ValidResultJson =
        """{"salary":{"min":120000,"max":160000,"currency":"USD","period":"Year","confidence":0.7},"isRemote":true,"isContractorFriendly":false,"timezoneBand":"EMEA","aiUsage":"High","companyStage":"SeriesB","technologies":["Go","Kubernetes"],"reasons":["Fully remote across EMEA","Building LLM inference infrastructure"]}""";

    // ---- Checkpoint 1: crash after the Run is created, before its scope is selected -----------------

    [RequiresDockerFact]
    public async Task Checkpoint_1_resumes_a_created_run_selecting_scope_once_and_submitting_once()
    {
        await using var h = await Harness.CreateAsync();
        // A Run persisted in Created with a stale scope, as a crash before scope selection would leave it.
        var runId = await h.SeedCreatedRunAsync(scope: 0);

        // The resume re-enters the Created Run: it recomputes scope (idempotent) and hands off to submission.
        await h.ResumeAsync(runId);
        (await h.LoadScopeAsync(runId)).ShouldBe(JobCount);

        await h.SubmitAsync(runId);
        await h.PollAsync(runId);
        await h.ProcessAsync(runId);

        await h.AssertPaidOnceAndAdvancedAsync(runId);
    }

    // ---- Checkpoint 2: crash after scope, before the estimate is ledgered --------------------------

    [RequiresDockerFact]
    public async Task Checkpoint_2_submits_once_and_writes_a_single_estimate_when_none_was_committed()
    {
        await using var h = await Harness.CreateAsync();
        var runId = await h.SeedCreatedRunAsync(scope: JobCount);

        // No Estimated entry yet — the crash fell before the first commit. Submission writes exactly one.
        await h.SubmitAsync(runId);
        await h.PollAsync(runId);
        await h.ProcessAsync(runId);

        (await h.LedgerCountAsync(runId, LedgerEntryKind.Estimated)).ShouldBe(1);
        await h.AssertPaidOnceAndAdvancedAsync(runId);
    }

    // ---- Checkpoint 3: crash after the estimate commits, before SubmitAsync ------------------------

    [RequiresDockerFact]
    public async Task Checkpoint_3_reuses_the_committed_estimate_and_submits_exactly_once()
    {
        await using var h = await Harness.CreateAsync();
        var runId = await h.SeedCreatedRunAsync(scope: JobCount);
        // The estimate committed in its own transaction; the provider was never reached (no orphan batch).
        await h.SeedEstimateAsync(runId, costUsd: 0.01m);

        await h.SubmitAsync(runId);
        await h.PollAsync(runId);
        await h.ProcessAsync(runId);

        // The pre-committed estimate is reused, not re-written, and the provider is paid exactly once.
        (await h.LedgerCountAsync(runId, LedgerEntryKind.Estimated)).ShouldBe(1);
        await h.AssertPaidOnceAndAdvancedAsync(runId);
    }

    // ---- Checkpoint 4: crash after SubmitAsync returns, before the batch row commits (D5) -----------

    [RequiresDockerFact]
    public async Task Checkpoint_4_adopts_the_orphaned_provider_batch_rather_than_resubmitting()
    {
        await using var h = await Harness.CreateAsync();
        var runId = await h.SeedCreatedRunAsync(scope: JobCount);
        // The estimate committed first (its own transaction), so the resume knows a prior attempt happened.
        await h.SeedEstimateAsync(runId, costUsd: 0.01m);

        // Simulate the lost submission: the provider accepted a batch (SubmitCallCount → 1, an orphan
        // recorded provider-side) but the process died before the batch row committed. No local batch row.
        await h.SimulateLostSubmissionAsync();
        h.Client.SubmitCallCount.ShouldBe(1);
        (await h.FindBatchAsync(runId)).ShouldBeNull();

        // The resume redelivery of the submission must reconcile: list the provider's recent batches, find
        // the orphan created since the Run began, and ADOPT it — never call SubmitAsync a second time.
        await h.SubmitAsync(runId);

        h.Client.SubmitCallCount.ShouldBe(1);       // the whole point of checkpoint 4 (D5)
        h.Client.ListCallCount.ShouldBeGreaterThan(0);
        var adopted = await h.FindBatchAsync(runId);
        adopted.ShouldNotBeNull();
        adopted!.ProviderBatchId.ShouldBe(h.Client.ProviderBatchId);

        await h.PollAsync(runId);
        await h.ProcessAsync(runId);
        await h.AssertPaidOnceAndAdvancedAsync(runId);
    }

    // ---- Checkpoint 5: crash after the batch row commits, before the first poll --------------------

    [RequiresDockerFact]
    public async Task Checkpoint_5_resumes_polling_from_the_persisted_provider_id_without_resubmitting()
    {
        await using var h = await Harness.CreateAsync();
        var runId = await h.SeedCreatedRunAsync(scope: JobCount);
        await h.SubmitAsync(runId);       // batch committed, Run now Enriching
        h.Client.SubmitCallCount.ShouldBe(1);

        // The crash fell before the first poll; the resume reaches the same batch by its persisted id.
        await h.PollAsync(runId);
        await h.ProcessAsync(runId);

        h.Client.SubmitCallCount.ShouldBe(1);
        await h.AssertPaidOnceAndAdvancedAsync(runId);
    }

    // ---- Checkpoint 6: crash mid-poll while the batch is still in progress --------------------------

    [RequiresDockerFact]
    public async Task Checkpoint_6_keeps_polling_a_still_running_batch_and_never_resubmits()
    {
        await using var h = await Harness.CreateAsync(pollsBeforeEnd: 1);
        var runId = await h.SeedCreatedRunAsync(scope: JobCount);
        await h.SubmitAsync(runId);

        // First poll observes InProgress and re-enqueues; a restart in between resumes the same batch.
        await h.PollAsync(runId);
        (await h.FindBatchAsync(runId))!.State.ShouldBe(BatchState.InProgress);
        // Second poll observes Ended and hands off.
        await h.PollAsync(runId);
        await h.ProcessAsync(runId);

        h.Client.SubmitCallCount.ShouldBe(1);
        await h.AssertPaidOnceAndAdvancedAsync(runId);
    }

    // ---- Checkpoint 7: crash mid-result-processing with some enrichments already stored -------------

    [RequiresDockerFact]
    public async Task Checkpoint_7_reprocessing_stores_the_remainder_without_duplicating_the_stored_items()
    {
        await using var h = await Harness.CreateAsync();
        var runId = await h.SeedCreatedRunAsync(scope: JobCount);
        await h.SubmitAsync(runId);
        await h.PollAsync(runId);

        // The enrichment upsert commits per item on its own connection, so a crash mid-stream can leave
        // some enrichments durably stored while the batch and Run stay un-advanced. Pre-store the first
        // item's enrichment on its (job_id, run_id) key to model exactly that.
        await h.SeedEnrichmentAsync(runId, h.JobIds[0]);

        // Reprocessing must store the remainder and no duplicate of the already-stored item (ON CONFLICT).
        await h.ProcessAsync(runId);

        (await h.EnrichmentCountAsync(runId)).ShouldBe(JobCount);
        await h.AssertPaidOnceAndAdvancedAsync(runId);
    }

    // ---- Checkpoint 8: crash after all items are stored, before the Run advances -------------------

    [RequiresDockerFact]
    public async Task Checkpoint_8_a_redelivery_after_completion_advances_the_run_once_and_changes_nothing()
    {
        await using var h = await Harness.CreateAsync();
        var runId = await h.SeedCreatedRunAsync(scope: JobCount);
        await h.SubmitAsync(runId);
        await h.PollAsync(runId);
        await h.ProcessAsync(runId);         // full pass: everything committed, Run → Matching
        await h.AssertPaidOnceAndAdvancedAsync(runId);

        // A redelivered BatchResultsReady after completion: batch already terminal, Run already advanced.
        await h.ProcessAsync(runId);

        (await h.EnrichmentCountAsync(runId)).ShouldBe(JobCount);
        await h.AssertPaidOnceAndAdvancedAsync(runId);   // still exactly one Actual entry, still Matching
    }

    // ---- Ledger equality: an interrupted run costs exactly what an uninterrupted one costs ----------

    [RequiresDockerFact]
    public async Task An_interrupted_run_ledgers_identically_to_an_uninterrupted_run()
    {
        // Uninterrupted baseline.
        await using var clean = await Harness.CreateAsync();
        var cleanRun = await clean.SeedCreatedRunAsync(scope: JobCount);
        await clean.SubmitAsync(cleanRun);
        await clean.PollAsync(cleanRun);
        await clean.ProcessAsync(cleanRun);
        var cleanTotal = await clean.ActualTotalAsync(cleanRun);

        // The checkpoint-4 interruption (adopt-not-resubmit) plus a redelivered final pass (checkpoint 8).
        await using var crashed = await Harness.CreateAsync();
        var crashedRun = await crashed.SeedCreatedRunAsync(scope: JobCount);
        await crashed.SeedEstimateAsync(crashedRun, costUsd: 0.01m);
        await crashed.SimulateLostSubmissionAsync();
        await crashed.SubmitAsync(crashedRun);        // adopts the orphan
        await crashed.PollAsync(crashedRun);
        await crashed.ProcessAsync(crashedRun);
        await crashed.ProcessAsync(crashedRun);       // idempotent redelivery
        var crashedTotal = await crashed.ActualTotalAsync(crashedRun);

        crashedTotal.ShouldBe(cleanTotal);
        crashedTotal.ShouldBe(ExpectedActualUsd);
        crashed.Client.SubmitCallCount.ShouldBe(1);
        clean.Client.SubmitCallCount.ShouldBe(1);
    }

    // ================================================================================================

    /// <summary>
    /// The crash-matrix harness: a real Postgres database, the real F3 collaborators, and a single counting
    /// fake provider client, clock and id generator shared across a Run (external and process state survives
    /// a crash). Every step is invoked through a <em>fresh</em> repository and context, which is exactly how
    /// a restarted worker re-reads the last committed state.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly TestDatabase _db;
        private readonly NpgsqlConnectionFactory _factory;
        private readonly FakeClock _clock = new(Now);
        private readonly SequentialIdGenerator _ids = new();
        private readonly IJitter _jitter = Substitute.For<IJitter>();

        public FakeLlmBatchClient Client { get; }

        public IMessageBus Bus { get; } = Substitute.For<IMessageBus>();

        public IReadOnlyList<Guid> JobIds { get; }

        private Harness(TestDatabase db, IReadOnlyList<Guid> jobIds, FakeLlmBatchClient client)
        {
            _db = db;
            _factory = new NpgsqlConnectionFactory(db.ConnectionString);
            JobIds = jobIds;
            Client = client;
            _jitter.Apply(Arg.Any<TimeSpan>()).Returns(ci => ci.Arg<TimeSpan>());
        }

        public static async Task<Harness> CreateAsync(int pollsBeforeEnd = 0)
        {
            var db = await TestDatabase.CreateAsync();
            var jobIds = await SeedJobsAsync(db);

            var results = jobIds
                .Select(id => new BatchResultItem(
                    id.ToString(), ValidResultJson, null, new TokenUsage(PerItemInputTokens, PerItemOutputTokens)))
                .ToList();

            var client = new FakeLlmBatchClient(results, pollsBeforeEnd)
            {
                ProviderBatchId = "msgbatch_crashmatrix_0001",
                // The reconciliation created-at bound is the Run's StartedAt; stamp the orphan at that instant
                // so ListRecentBatchesAsync finds it (checkpoint 4).
                ProviderCreatedAt = Now,
            };

            return new Harness(db, jobIds, client);
        }

        // ---- step drivers (each on a fresh repository/context, i.e. a restarted worker) --------------

        public Task ResumeAsync(Guid runId) =>
            new RunOrchestrator(Runs(), new LiveJobsQuery(_factory), _clock, _ids, NullLogger<RunOrchestrator>.Instance)
                .Handle(new ResumeRun(runId), Bus, CancellationToken.None);

        public Task SubmitAsync(Guid runId) =>
            new EnrichmentSubmitHandler(
                    Runs(), new EnrichmentScopeQuery(_factory), new EnrichmentRequestBuilder(),
                    Accountant(), Client, _clock, _ids, NullLogger<EnrichmentSubmitHandler>.Instance)
                .Handle(new EnrichmentSubmissionDue(runId), Bus, CancellationToken.None);

        public Task PollAsync(Guid runId) =>
            new BatchPollHandler(
                    Runs(), Client, _jitter, _clock,
                    new PollOptions { DeliveryDeadlineLocalTime = null, MaxPollDuration = TimeSpan.FromHours(6) },
                    NullLogger<BatchPollHandler>.Instance)
                .Handle(new BatchPollDue(runId), Bus, CancellationToken.None);

        public async Task ProcessAsync(Guid runId)
        {
            // The handler re-finds the batch itself; these values only ride along on the message.
            var batch = await FindBatchAsync(runId);
            var providerBatchId = batch?.ProviderBatchId ?? Client.ProviderBatchId;
            var batchId = batch?.Id ?? Guid.Empty;
            await new BatchResultProcessingHandler(
                    Runs(), Enrichments(), new EnrichmentResultParser(), Client, Accountant(), _clock, _ids,
                    NullLogger<BatchResultProcessingHandler>.Instance)
                .Handle(new BatchResultsReady(runId, batchId, providerBatchId), Bus, CancellationToken.None);
        }

        // ---- durable-state reconstruction (what a given crash boundary leaves in the database) -------

        public async Task<Guid> SeedCreatedRunAsync(int scope)
        {
            var runId = _ids.NewId();
            var run = new Run(runId, Now.AddDays(-1), Now, ceilingUsd: 2.00m, Now);
            run.SetScope(scope);
            var runs = Runs();
            runs.Add(run);
            await runs.SaveChangesAsync();
            return runId;
        }

        public async Task SeedEstimateAsync(Guid runId, decimal costUsd)
        {
            var runs = Runs();
            runs.AddLedgerEntry(new CostLedgerEntry(
                _ids.NewId(), runId, batchId: null, Stage, Tier, LedgerEntryKind.Estimated,
                costUsd, inputTokens: 12000, outputTokens: 1200, Now));
            var run = await runs.FindAsync(runId);
            run!.SetSpend(costUsd);
            await runs.SaveChangesAsync();
        }

        public async Task SimulateLostSubmissionAsync()
        {
            // The provider accepts the batch and records it, but the process dies before the local batch row
            // commits — that is the crash window checkpoint 4 covers. The items are irrelevant to adoption.
            _ = await Client.SubmitAsync(
                new BatchSubmission(Tier, EnrichmentPrompt.PromptVersion, []), CancellationToken.None);
        }

        public async Task SeedEnrichmentAsync(Guid runId, Guid jobId)
        {
            var enrichment = new EnrichmentAggregate(
                _ids.NewId(), jobId, runId, salary: null, isRemote: true, isContractorFriendly: false,
                JobHunter.Domain.Intelligence.TimezoneBand.EMEA, JobHunter.Domain.Intelligence.AiUsageLevel.Medium,
                JobHunter.Domain.Intelligence.CompanyStage.SeriesB,
                JobHunter.Domain.Intelligence.RoleFamily.Platform, technologies: ["Go"],
                reasons: ["pre-stored before the crash"], EnrichmentPrompt.PromptVersion, Now);
            (await Enrichments().UpsertAsync(enrichment)).ShouldBeTrue();
        }

        // ---- read helpers ----------------------------------------------------------------------------

        public Task<Batch?> FindBatchAsync(Guid runId) => Runs().FindBatchAsync(runId, Stage, Tier);

        public async Task<int> LoadScopeAsync(Guid runId) => (await Runs().FindAsync(runId))!.JobsInScope;

        public async Task<int> LedgerCountAsync(Guid runId, LedgerEntryKind kind)
        {
            await using var ctx = _db.CreateContext();
            return await ctx.Set<CostLedgerEntry>().CountAsync(e => e.RunId == runId && e.Kind == kind);
        }

        public async Task<decimal> ActualTotalAsync(Guid runId)
        {
            await using var ctx = _db.CreateContext();
            var entries = await ctx.Set<CostLedgerEntry>()
                .Where(e => e.RunId == runId && e.Kind == LedgerEntryKind.Actual)
                .ToListAsync();
            return entries.Sum(e => e.CostUsd);
        }

        public async Task<int> EnrichmentCountAsync(Guid runId)
        {
            await using var ctx = _db.CreateContext();
            return await ctx.Set<EnrichmentAggregate>().CountAsync(e => e.RunId == runId);
        }

        /// <summary>Every checkpoint's shared invariant: paid once, one Actual entry equal to baseline, advanced.</summary>
        public async Task AssertPaidOnceAndAdvancedAsync(Guid runId)
        {
            Client.SubmitCallCount.ShouldBe(1, "the provider must be paid exactly once across the whole Run (QG-1).");
            (await LedgerCountAsync(runId, LedgerEntryKind.Actual)).ShouldBe(1, "exactly one Actual ledger entry.");
            (await ActualTotalAsync(runId)).ShouldBe(ExpectedActualUsd, "the interrupted Run costs the uninterrupted total.");
            (await Runs().FindAsync(runId))!.State.ShouldBe(RunState.Matching);
        }

        private RunRepository Runs() => new(_db.CreateContext());

        private EnrichmentRepository Enrichments() => new(_db.CreateContext(), _factory);

        private static CostAccountant Accountant() => new(new HeuristicTokenCounter(), Options.Create(Pricing));

        private static async Task<IReadOnlyList<Guid>> SeedJobsAsync(TestDatabase db)
        {
            var companyId = Guid.CreateVersion7();
            var bindingId = Guid.CreateVersion7();
            var sourceId = Guid.CreateVersion7();
            var jobIds = new List<Guid>();

            await using var ctx = db.CreateContext();
            ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now));
            ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", Now));
            ctx.Add(new JobSource(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs"));

            for (var i = 0; i < JobCount; i++)
            {
                var rawPostingId = Guid.CreateVersion7();
                var jobId = Guid.CreateVersion7();
                jobIds.Add(jobId);
                ctx.Add(new RawPosting(
                    rawPostingId, sourceId, $"job-{i}", ContentHash.Compute($"{{\"t\":\"{i}\"}}"), $"{{\"t\":\"{i}\"}}", 200, Now));
                ctx.Add(new Job(
                    jobId, companyId, rawPostingId, Fingerprint.TryCreate(new string((char)('a' + i), 64)).Value,
                    fingerprintVersion: 1, $"Staff Engineer {i}", normalisedTitle: $"staff engineer {i}",
                    description: "Build and operate LLM inference infrastructure on Kubernetes.",
                    applyUrl: $"https://acme.com/apply/{i}",
                    LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
                    RemotePolicy.Remote, EmploymentType.FullTime, PostedAtGranularity.Day,
                    firstSeenAt: Now.AddHours(-2), lastSeenAt: Now.AddHours(-2)));
            }

            await ctx.SaveChangesAsync();
            return jobIds;
        }

        public ValueTask DisposeAsync() => _db.DisposeAsync();
    }
}
