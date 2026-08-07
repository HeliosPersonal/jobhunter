using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Reporting;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// T02: the F5 persistence. The migration creates <c>digests</c>, <c>digest_cards</c> and
/// <c>delivery_log</c> with all six declared indexes. The one that carries a rule is the unique
/// <c>uq_delivery_log</c> on <c>(run_id, chat_id, card_key)</c> — [[CONTEXT]] invariant 8 — asserted by
/// a second raw insert that <c>ON CONFLICT DO NOTHING</c> declines, so the log stays append-only with no
/// update and no delete. The already-delivered read is covered by <c>idx_delivery_log_run_chat</c>,
/// verified by a query plan. One digest per Run is enforced by <c>uq_digests_run</c>. Requires Docker.
/// </summary>
public sealed class DigestPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 6, 0, 0, TimeSpan.Zero);

    private static readonly string[] ExpectedIndexes =
    [
        "uq_delivery_log",
        "idx_delivery_log_run_chat",
        "uq_digests_run",
        "uq_digest_cards_job",
        "uq_digest_cards_key",
        "idx_digest_cards_rank",
    ];

    [RequiresDockerFact]
    public async Task All_six_declared_indexes_exist_after_the_migration()
    {
        await using var database = await TestDatabase.CreateAsync();

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT indexname FROM pg_indexes WHERE schemaname = 'public' AND indexname = ANY(@names)";
        command.Parameters.AddWithValue("names", ExpectedIndexes);

        var found = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            found.Add(reader.GetString(0));
        }

        found.OrderBy(x => x).ShouldBe(ExpectedIndexes.OrderBy(x => x));
    }

    [RequiresDockerFact]
    public async Task A_digest_and_its_cards_round_trip_rank_ordered()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var repo = new DigestRepository(seed.Database.CreateContext());
        repo.Add(seed.NewDigest());
        await repo.SaveChangesAsync();

        var read = new DigestRepository(seed.Database.CreateContext());
        var loaded = await read.FindByRunAsync(seed.RunId);

        loaded.ShouldNotBeNull();
        loaded.RunId.ShouldBe(seed.RunId);
        loaded.SuppressedCount.ShouldBe(3);
        loaded.SuppressionBreakdown.Select(t => t.Reason).ShouldBe(["salary floor", "location"]);
        loaded.DegradedSources.ShouldBe(["greenhouse:flaky"]);
        loaded.NarrativeSource.ShouldBe(NarrativeSource.Model);
        loaded.PromptVersion.ShouldBe("digest-v1");
        loaded.Cards.Select(c => c.Rank).ShouldBe([1]);
        loaded.Cards[0].Reasons.ShouldHaveSingleItem();
        loaded.Cards[0].Key.Value.ShouldBe(CardKey.For(seed.RunId, seed.JobId).Value);
        // Learning was on when this digest was assembled (the default); it round-trips as such (AC-07).
        loaded.LearningEnabled.ShouldBeTrue();
    }

    [RequiresDockerFact]
    public async Task A_learning_off_digest_persists_that_state_for_replay()
    {
        // AC-07: a digest assembled while learning was off keeps saying so on a later re-render — the state
        // is frozen in the column (S2), not re-derived from the live switch at send time.
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var repo = new DigestRepository(seed.Database.CreateContext());
        repo.Add(seed.NewDigest(learningEnabled: false));
        await repo.SaveChangesAsync();

        var read = new DigestRepository(seed.Database.CreateContext());
        var loaded = await read.FindByRunAsync(seed.RunId);

        loaded.ShouldNotBeNull();
        loaded.LearningEnabled.ShouldBeFalse();
    }

    [RequiresDockerFact]
    public async Task A_cards_grouped_away_jobs_round_trip_through_the_jsonb_column()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var groupedAway = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };

        var repo = new DigestRepository(seed.Database.CreateContext());
        repo.Add(seed.NewDigest(groupedJobIds: groupedAway));
        await repo.SaveChangesAsync();

        var read = new DigestRepository(seed.Database.CreateContext());
        var loaded = await read.FindByRunAsync(seed.RunId);

        // The near-duplicate jobs a card grouped away survive the round-trip (F5-T13): grouped, never dropped,
        // so they stay queryable off the persisted digest.
        loaded.ShouldNotBeNull();
        loaded.Cards.ShouldHaveSingleItem().GroupedJobIds.ShouldBe(groupedAway);
    }

    [RequiresDockerFact]
    public async Task A_card_that_groups_nothing_round_trips_an_empty_list()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var repo = new DigestRepository(seed.Database.CreateContext());
        repo.Add(seed.NewDigest());
        await repo.SaveChangesAsync();

        var read = new DigestRepository(seed.Database.CreateContext());
        var loaded = await read.FindByRunAsync(seed.RunId);

        // The common case — a card that stands alone — stores and reads back an empty jsonb array, not null.
        loaded.ShouldNotBeNull();
        loaded.Cards.ShouldHaveSingleItem().GroupedJobIds.ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task A_second_digest_for_the_same_run_is_rejected_by_uq_digests_run()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var first = new DigestRepository(seed.Database.CreateContext());
        first.Add(seed.NewDigest());
        await first.SaveChangesAsync();

        // One digest per Run (uq_digests_run) — a second assembly for the same Run fails at commit.
        var second = new DigestRepository(seed.Database.CreateContext());
        second.Add(seed.NewDigest());
        var ex = await Should.ThrowAsync<DbUpdateException>(() => second.SaveChangesAsync());

        var postgres = ex.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe("uq_digests_run");
    }

    [RequiresDockerFact]
    public async Task A_first_delivery_records_and_a_replay_is_the_idempotent_no_op()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var log = new DeliveryLog(new NpgsqlConnectionFactory(seed.Database.ConnectionString));

        var record = seed.NewDelivery();
        // The first send writes a row; the replay of the same (run_id, chat_id, card_key) writes nothing.
        (await log.TryRecordAsync(record)).ShouldBeTrue();
        (await log.TryRecordAsync(seed.NewDelivery())).ShouldBeFalse();

        (await log.DeliveredKeysAsync(seed.RunId, seed.ChatId)).ShouldBe([record.CardKey.Value]);
    }

    [RequiresDockerFact]
    public async Task A_duplicate_delivery_row_is_rejected_by_the_unique_constraint()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var log = new DeliveryLog(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        await log.TryRecordAsync(seed.NewDelivery());

        // A raw insert that bypasses ON CONFLICT proves the database — not the repository — is the arbiter
        // of invariant 8: the unique (run_id, chat_id, card_key) index rejects the duplicate.
        await using var connection = new NpgsqlConnection(seed.Database.ConnectionString);
        await connection.OpenAsync();
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            "INSERT INTO delivery_log (id, run_id, chat_id, card_key, telegram_message_id, delivered_at) " +
            "VALUES (@id, @run_id, @chat_id, @card_key, NULL, @delivered_at)";
        insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("run_id", seed.RunId);
        insert.Parameters.AddWithValue("chat_id", seed.ChatId);
        insert.Parameters.AddWithValue("card_key", CardKey.For(seed.RunId, seed.JobId).Value);
        insert.Parameters.AddWithValue("delivered_at", Now);

        var ex = await Should.ThrowAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        ex.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        ex.ConstraintName.ShouldBe("uq_delivery_log");
    }

    [RequiresDockerFact]
    public void The_delivery_log_exposes_no_update_and_no_delete_path()
    {
        // Invariant 8 as an API property: deleting a row would mean re-delivering, the exact failure the log
        // prevents. The port and its implementation offer only a record and a read of what was recorded.
        typeof(IDeliveryLog).GetMethods()
            .Select(m => m.Name)
            .ShouldBe(["TryRecordAsync", "DeliveredKeysAsync"], ignoreOrder: true);

        typeof(DeliveryLog).GetMethods()
            .Where(m => m.DeclaringType == typeof(DeliveryLog))
            .Select(m => m.Name)
            .ShouldBe(["TryRecordAsync", "DeliveredKeysAsync"], ignoreOrder: true);
    }

    [RequiresDockerFact]
    public async Task The_already_delivered_query_is_index_covered_on_run_and_chat()
    {
        // AC: the "what have I already sent for this Run and chat" read on resume must be index-served, not a
        // seq scan. Two indexes lead with (run_id, chat_id) — idx_delivery_log_run_chat and the covering
        // uq_delivery_log — and the planner is free to pick whichever is cheaper; both satisfy the coverage
        // promise. What must never happen is a sequential scan of the whole log.
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var log = new DeliveryLog(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        await log.TryRecordAsync(seed.NewDelivery());

        await using var connection = new NpgsqlConnection(seed.Database.ConnectionString);
        await connection.OpenAsync();

        // Force an index path so the planner cannot fall back to a seq scan on a near-empty table.
        await using (var noSeq = connection.CreateCommand())
        {
            noSeq.CommandText = "SET enable_seqscan = off;";
            await noSeq.ExecuteNonQueryAsync();
        }

        await using var explain = connection.CreateCommand();
        explain.CommandText =
            "EXPLAIN SELECT card_key FROM delivery_log WHERE run_id = @run AND chat_id = @chat";
        explain.Parameters.AddWithValue("run", seed.RunId);
        explain.Parameters.AddWithValue("chat", seed.ChatId);

        var plan = new System.Text.StringBuilder();
        await using var reader = await explain.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            plan.AppendLine(reader.GetString(0));
        }

        var text = plan.ToString();
        text.ShouldNotContain("Seq Scan");
        (text.Contains("idx_delivery_log_run_chat", StringComparison.Ordinal)
            || text.Contains("uq_delivery_log", StringComparison.Ordinal)).ShouldBeTrue();
    }

    private static async Task<Seed> SeedAsync()
    {
        var database = await TestDatabase.CreateAsync();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var rawPostingId = Guid.CreateVersion7();
        var jobId = Guid.CreateVersion7();
        var runId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now));
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", Now));
        ctx.Add(new JobSource(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs"));
        ctx.Add(new RawPosting(rawPostingId, sourceId, "job-1", ContentHash.Compute("{\"t\":\"x\"}"), "{\"t\":\"x\"}", 200, Now));
        ctx.Add(new Job(
            jobId, companyId, rawPostingId, Fingerprint.TryCreate(new string('a', 64)).Value,
            fingerprintVersion: 1, "Staff SRE", normalisedTitle: "staff sre", description: "d",
            applyUrl: "https://acme.com/apply/1", LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            RemotePolicy.Hybrid, EmploymentType.FullTime, PostedAtGranularity.Day, firstSeenAt: Now, lastSeenAt: Now));
        ctx.Add(new Run(runId, Now.AddDays(-1), Now, ceilingUsd: 5m, Now));
        await ctx.SaveChangesAsync();

        return new Seed(database, jobId, runId, ChatId: 123456789L);
    }

    private sealed record Seed(TestDatabase Database, Guid JobId, Guid RunId, long ChatId)
    {
        public Digest NewDigest(IReadOnlyList<Guid>? groupedJobIds = null, bool learningEnabled = true)
        {
            var digestId = Guid.CreateVersion7();
            var card = new DigestCard(
                Guid.CreateVersion7(), digestId, JobId, RunId, rank: 1, score: 82m,
                reasons: ["Strong platform-engineering overlap."], applyUrlVerified: true,
                groupedJobIds: groupedJobIds);
            return new Digest(
                digestId, RunId, DigestMode.Full, totalNewJobs: 10, strongMatches: 1, avgSalaryUsd: 120000m,
                suppressedCount: 3,
                suppressionBreakdown:
                [
                    SuppressionTally.TryCreate("salary floor", 2).Value,
                    SuppressionTally.TryCreate("location", 1).Value,
                ],
                carriedOverCount: 0, companiesChecked: 0, analysedCount: 0, degradedSources: ["greenhouse:flaky"],
                narrative: "A quiet day with one strong lead.", NarrativeSource.Model,
                promptVersion: "digest-v1", cards: [card], generatedAt: Now, restoredCount: 0,
                learningEnabled: learningEnabled);
        }

        public DeliveryRecord NewDelivery() =>
            new(Guid.CreateVersion7(), RunId, ChatId, CardKey.For(RunId, JobId), telegramMessageId: 42L, Now);
    }
}
