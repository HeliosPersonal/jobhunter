using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// T04: the Run/Batch/ledger persistence. The two invariant-carrying indexes are asserted directly —
/// <c>uq_runs_single_active</c> rejects a second live Run (QG-1), <c>uq_batches_run_stage_tier</c> rejects
/// a second batch for the same run, stage and tier (no double submission) — the resumable-Runs query is
/// covered by <c>idx_runs_resumable</c> (verified with a query plan), and the ledger has no update or
/// delete path. Requires Docker.
/// </summary>
public sealed class RunPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    private static Run NewRun(DateTimeOffset? startedAt = null) =>
        new(Guid.CreateVersion7(), Now.AddDays(-1), Now, ceilingUsd: 5.00m, startedAt ?? Now);

    private static Batch NewBatch(Guid runId, BatchStage stage = BatchStage.Enrichment, ModelTier tier = ModelTier.Cheap) =>
        new(Guid.CreateVersion7(), runId, stage, tier, providerBatchId: $"prov-{Guid.NewGuid():N}", promptVersion: "enrich-v1", itemCount: 3, Now);

    [RequiresDockerFact]
    public async Task A_run_persists_with_its_batches_items_and_ledger_entries()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var seed = await SeedJobAsync(database);

        var run = NewRun();
        var batch = NewBatch(run.Id);
        var item = new BatchItem(Guid.CreateVersion7(), batch.Id, seed.JobId.ToString(), seed.JobId);
        var estimate = new CostLedgerEntry(
            Guid.CreateVersion7(), run.Id, batch.Id, BatchStage.Enrichment, ModelTier.Cheap,
            LedgerEntryKind.Estimated, costUsd: 0.15m, inputTokens: 1200, outputTokens: 400, Now);

        var repo = new RunRepository(database.CreateContext());
        repo.Add(run);
        repo.AddBatch(batch);
        repo.AddBatchItem(item);
        repo.AddLedgerEntry(estimate);
        await repo.SaveChangesAsync();

        await using var read = database.CreateContext();
        (await read.Set<Run>().CountAsync()).ShouldBe(1);
        (await read.Set<Batch>().SingleAsync()).ProviderBatchId.ShouldBe(batch.ProviderBatchId);
        (await read.Set<BatchItem>().SingleAsync()).CustomId.ShouldBe(seed.JobId.ToString());
        (await read.Set<CostLedgerEntry>().SingleAsync()).Kind.ShouldBe(LedgerEntryKind.Estimated);
    }

    [RequiresDockerFact]
    public async Task A_second_non_terminal_run_is_rejected_by_the_partial_unique_index()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;

        var repo = new RunRepository(database.CreateContext());
        repo.Add(NewRun());
        await repo.SaveChangesAsync();

        var second = new RunRepository(database.CreateContext());
        second.Add(NewRun(startedAt: Now.AddMinutes(1)));

        var ex = await Should.ThrowAsync<DbUpdateException>(() => second.SaveChangesAsync());
        var pg = ex.InnerException.ShouldBeOfType<Npgsql.PostgresException>();
        pg.SqlState.ShouldBe(Npgsql.PostgresErrorCodes.UniqueViolation);
        pg.ConstraintName.ShouldBe("uq_runs_single_active");
    }

    [RequiresDockerFact]
    public async Task A_second_run_is_allowed_once_the_first_reaches_a_terminal_state()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;

        var first = NewRun();
        // Walk the Run to a terminal state so the partial index no longer covers it.
        first.Abort("done", Now.AddHours(1), costBreach: false);
        var repo = new RunRepository(database.CreateContext());
        repo.Add(first);
        await repo.SaveChangesAsync();

        var second = new RunRepository(database.CreateContext());
        second.Add(NewRun(startedAt: Now.AddHours(2)));
        await Should.NotThrowAsync(() => second.SaveChangesAsync());

        await using var read = database.CreateContext();
        (await read.Set<Run>().CountAsync()).ShouldBe(2);
    }

    [RequiresDockerFact]
    public async Task A_second_batch_for_the_same_run_stage_and_tier_is_rejected()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;

        var run = NewRun();
        var repo = new RunRepository(database.CreateContext());
        repo.Add(run);
        repo.AddBatch(NewBatch(run.Id, BatchStage.Enrichment, ModelTier.Cheap));
        await repo.SaveChangesAsync();

        var second = new RunRepository(database.CreateContext());
        second.AddBatch(NewBatch(run.Id, BatchStage.Enrichment, ModelTier.Cheap));

        var ex = await Should.ThrowAsync<DbUpdateException>(() => second.SaveChangesAsync());
        var pg = ex.InnerException.ShouldBeOfType<Npgsql.PostgresException>();
        pg.SqlState.ShouldBe(Npgsql.PostgresErrorCodes.UniqueViolation);
        pg.ConstraintName.ShouldBe("uq_batches_run_stage_tier");
    }

    [RequiresDockerFact]
    public async Task A_batch_differing_only_in_tier_is_allowed_for_the_same_run_and_stage()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;

        var run = NewRun();
        var repo = new RunRepository(database.CreateContext());
        repo.Add(run);
        repo.AddBatch(NewBatch(run.Id, BatchStage.Enrichment, ModelTier.Cheap));
        repo.AddBatch(NewBatch(run.Id, BatchStage.Enrichment, ModelTier.Deep));
        await Should.NotThrowAsync(() => repo.SaveChangesAsync());

        await using var read = database.CreateContext();
        (await read.Set<Batch>().CountAsync()).ShouldBe(2);
    }

    [RequiresDockerFact]
    public async Task FindResumableRuns_returns_only_non_terminal_runs()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;

        // One terminal Run (does not block the live one and is not resumable).
        var terminal = NewRun();
        terminal.Abort("done", Now, costBreach: false);
        var terminalRepo = new RunRepository(database.CreateContext());
        terminalRepo.Add(terminal);
        await terminalRepo.SaveChangesAsync();

        var live = NewRun(startedAt: Now.AddMinutes(5));
        var liveRepo = new RunRepository(database.CreateContext());
        liveRepo.Add(live);
        await liveRepo.SaveChangesAsync();

        var reader = new RunRepository(database.CreateContext());
        var resumable = await reader.FindResumableRunsAsync();
        resumable.ShouldHaveSingleItem().Id.ShouldBe(live.Id);

        (await reader.FindActiveRunAsync())!.Id.ShouldBe(live.Id);
    }

    [RequiresDockerFact]
    public async Task The_resumable_runs_query_is_covered_by_its_partial_index()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;

        var repo = new RunRepository(database.CreateContext());
        repo.Add(NewRun());
        await repo.SaveChangesAsync();

        await using var connection = new Npgsql.NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        // Force an index scan so the planner's choice reflects index availability, not table size.
        await using (var settings = connection.CreateCommand())
        {
            settings.CommandText = "SET enable_seqscan = off;";
            await settings.ExecuteNonQueryAsync();
        }

        await using var explain = connection.CreateCommand();
        explain.CommandText =
            "EXPLAIN SELECT id FROM runs WHERE state NOT IN ('Delivered', 'Failed', 'CostAborted') ORDER BY started_at";

        var plan = new System.Text.StringBuilder();
        await using (var reader = await explain.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                plan.AppendLine(reader.GetString(0));
            }
        }

        plan.ToString().ShouldContain("idx_runs_resumable");
    }

    [RequiresDockerFact]
    public async Task The_run_repository_offers_no_delete_or_update_path_for_the_ledger()
    {
        // The ledger is append-only by construction: IRunRepository exposes AddLedgerEntry and nothing
        // else. This is a compile-time guarantee asserted here so a later hand adding a delete is caught.
        var methods = typeof(Domain.Abstractions.IRunRepository)
            .GetMethods()
            .Select(m => m.Name)
            .ToArray();

        methods.ShouldContain("AddLedgerEntry");
        methods.ShouldNotContain(n => n.Contains("Delete", StringComparison.OrdinalIgnoreCase));
        methods.ShouldNotContain(n => n.Contains("Remove", StringComparison.OrdinalIgnoreCase));
        methods.ShouldNotContain(n => n.Contains("Update", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<Seed> SeedJobAsync(TestDatabase database)
    {
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var rawPostingId = Guid.CreateVersion7();
        var jobId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now));
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", Now));
        ctx.Add(new JobSource(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs"));
        ctx.Add(new RawPosting(rawPostingId, sourceId, "job-1", ContentHash.Compute("{\"t\":\"x\"}"), "{\"t\":\"x\"}", 200, Now));

        var job = new Job(
            jobId, companyId, rawPostingId, Fingerprint.TryCreate(new string('a', 64)).Value,
            fingerprintVersion: 1, "Staff SRE", normalisedTitle: "staff sre", description: "d",
            applyUrl: "https://acme.com/apply/1", LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            RemotePolicy.Hybrid, EmploymentType.FullTime, PostedAtGranularity.Day, firstSeenAt: Now, lastSeenAt: Now);
        ctx.Add(job);
        await ctx.SaveChangesAsync();

        return new Seed(jobId);
    }

    private sealed record Seed(Guid JobId);
}
