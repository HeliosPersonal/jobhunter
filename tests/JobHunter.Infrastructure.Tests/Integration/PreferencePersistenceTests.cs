using System.Text;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Preferences;
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
/// T02: the F7 persistence. The migration creates <c>signals</c>, <c>preference_models</c>,
/// <c>preference_weights</c> and <c>suppression_overrides</c> with all seven declared indexes. The two that
/// carry rules are asserted directly: <c>uq_signals_action</c> on <c>(job_id, kind, occurred_at)</c> makes
/// capture idempotent, and <c>uq_preference_models_active</c> — the partial unique index — enforces exactly
/// one active model. The fitting-window read is covered by <c>idx_signals_window</c> and the per-job weight
/// lookup by <c>idx_preference_weights_lookup</c>, both verified with a query plan. Requires Docker.
/// </summary>
public sealed class PreferencePersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 6, 0, 0, TimeSpan.Zero);

    private static readonly string[] ExpectedIndexes =
    [
        "uq_signals_action",
        "idx_signals_window",
        "idx_signals_kind",
        "uq_preference_models_active",
        "uq_preference_models_version",
        "idx_preference_weights_lookup",
        "uq_suppression_overrides",
    ];

    private static JobFacts Facts() => JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
    {
        [Dimension.Country] = ["DE"],
        [Dimension.Technology] = ["Kafka", "Go"],
    });

    [RequiresDockerFact]
    public async Task All_seven_declared_indexes_exist_after_the_migration()
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
    public async Task A_signal_round_trips_with_its_job_facts_snapshot()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = new SignalRepository(new NpgsqlConnectionFactory(seed.Database.ConnectionString));

        var signal = Signal.Capture(
            Guid.CreateVersion7(), seed.JobId, applicationId: null, SignalKind.Saved, Facts(), Now, SignalWeights.Default);
        (await repo.TryCaptureAsync(signal)).ShouldBeTrue();

        await using var read = seed.Database.CreateContext();
        var stored = await read.Set<Signal>().SingleAsync();
        stored.Kind.ShouldBe(SignalKind.Saved);
        stored.Weight.ShouldBe(1.0m);
        stored.JobFacts.ValuesFor(Dimension.Country).ShouldBe(["DE"]);
        stored.JobFacts.ValuesFor(Dimension.Technology).ShouldBe(["Kafka", "Go"]);
    }

    [RequiresDockerFact]
    public async Task A_redelivered_action_captures_no_second_signal()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = new SignalRepository(new NpgsqlConnectionFactory(seed.Database.ConnectionString));

        var first = Signal.Capture(
            Guid.CreateVersion7(), seed.JobId, null, SignalKind.Opened, Facts(), Now, SignalWeights.Default);
        // A distinct id but the same (job_id, kind, occurred_at): the redelivery of one card action.
        var replay = Signal.Capture(
            Guid.CreateVersion7(), seed.JobId, null, SignalKind.Opened, Facts(), Now, SignalWeights.Default);

        (await repo.TryCaptureAsync(first)).ShouldBeTrue();
        (await repo.TryCaptureAsync(replay)).ShouldBeFalse();

        await using var read = seed.Database.CreateContext();
        (await read.Set<Signal>().CountAsync()).ShouldBe(1);
    }

    [RequiresDockerFact]
    public async Task A_duplicate_signal_is_rejected_by_the_unique_constraint()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = new SignalRepository(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        await repo.TryCaptureAsync(Signal.Capture(
            Guid.CreateVersion7(), seed.JobId, null, SignalKind.Opened, Facts(), Now, SignalWeights.Default));

        // A raw insert that bypasses ON CONFLICT proves the database is the arbiter of idempotence.
        await using var connection = new NpgsqlConnection(seed.Database.ConnectionString);
        await connection.OpenAsync();
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            "INSERT INTO signals (id, job_id, application_id, kind, weight, job_facts, occurred_at) " +
            "VALUES (@id, @job_id, NULL, @kind, @weight, @facts::jsonb, @occurred_at)";
        insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("job_id", seed.JobId);
        insert.Parameters.AddWithValue("kind", SignalKind.Opened.ToString());
        insert.Parameters.AddWithValue("weight", 1.0m);
        insert.Parameters.AddWithValue("facts", "{\"Country\":[\"DE\"]}");
        insert.Parameters.AddWithValue("occurred_at", Now);

        var ex = await Should.ThrowAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        ex.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        ex.ConstraintName.ShouldBe("uq_signals_action");
    }

    [RequiresDockerFact]
    public async Task A_model_round_trips_with_its_weights_and_evidence()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var signals = await SeedSignalsAsync(seed, count: 3);

        var modelId = Guid.CreateVersion7();
        var weight = new PreferenceWeight(
            Guid.CreateVersion7(), modelId, Dimension.Country, "DE", -0.4m, signals, positiveRate: 0.08m, Now);
        var model = new PreferenceModel(modelId, version: 1, signalCount: 250, [weight], Now);

        var repo = new PreferenceModelRepository(seed.Database.CreateContext());
        repo.Add(model);
        await repo.SaveChangesAsync();

        // The model was inserted inactive (no Activate call), so read it directly with its weights.
        await using var ctx = seed.Database.CreateContext();
        var loaded = await ctx.Set<PreferenceModel>().Include(m => m.Weights).SingleAsync();
        loaded.Version.ShouldBe(1);
        loaded.SignalCount.ShouldBe(250);
        var storedWeight = loaded.Weights.ShouldHaveSingleItem();
        storedWeight.Value.ShouldBe("DE");
        storedWeight.Weight.ShouldBe(-0.4m);
        storedWeight.SupportingSignalIds.ShouldBe(signals, ignoreOrder: true);
        storedWeight.SupportingSignalCount.ShouldBe(3);

        var read = new PreferenceModelRepository(seed.Database.CreateContext());
        (await read.LatestVersionAsync()).ShouldBe(1);
    }

    [RequiresDockerFact]
    public async Task Exactly_one_model_may_be_active()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var first = NewActiveModel(version: 1);
        var firstRepo = new PreferenceModelRepository(seed.Database.CreateContext());
        firstRepo.Add(first);
        await firstRepo.SaveChangesAsync();

        // A second active model without deactivating the first trips the partial unique index.
        var second = NewActiveModel(version: 2);
        var secondRepo = new PreferenceModelRepository(seed.Database.CreateContext());
        secondRepo.Add(second);

        var ex = await Should.ThrowAsync<DbUpdateException>(() => secondRepo.SaveChangesAsync());
        var pg = ex.InnerException.ShouldBeOfType<PostgresException>();
        pg.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        pg.ConstraintName.ShouldBe("uq_preference_models_active");
    }

    [RequiresDockerFact]
    public async Task A_refit_flips_activation_atomically_and_the_active_read_returns_the_new_version()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var v1 = NewActiveModel(version: 1);
        var setup = new PreferenceModelRepository(seed.Database.CreateContext());
        setup.Add(v1);
        await setup.SaveChangesAsync();

        // The weekly refit (SAD §4 S6): deactivate the prior active version and activate the new one in one
        // transaction, so uq_preference_models_active is never momentarily violated.
        var refit = new PreferenceModelRepository(seed.Database.CreateContext());
        var active = await refit.FindActiveAsync();
        active!.Deactivate();
        var v2 = NewActiveModel(version: 2);
        refit.Add(v2);
        await refit.SaveChangesAsync();

        var reader = new PreferenceModelRepository(seed.Database.CreateContext());
        (await reader.FindActiveAsync())!.Version.ShouldBe(2);
        (await reader.LatestVersionAsync()).ShouldBe(2);
    }

    [RequiresDockerFact]
    public async Task A_second_override_for_the_same_value_is_rejected()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        await using var ctx = seed.Database.CreateContext();
        ctx.Add(new SuppressionOverride(Guid.CreateVersion7(), Dimension.Country, "DE", SuppressionMode.NeverSuppress, Now));
        await ctx.SaveChangesAsync();

        await using var conflict = seed.Database.CreateContext();
        conflict.Add(new SuppressionOverride(Guid.CreateVersion7(), Dimension.Country, "DE", SuppressionMode.AlwaysSuppress, Now));
        var ex = await Should.ThrowAsync<DbUpdateException>(() => conflict.SaveChangesAsync());
        var pg = ex.InnerException.ShouldBeOfType<PostgresException>();
        pg.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        pg.ConstraintName.ShouldBe("uq_suppression_overrides");
    }

    [RequiresDockerFact]
    public async Task The_fitting_window_query_is_covered_by_its_index()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        await SeedSignalsAsync(seed, count: 1);

        var plan = await ExplainAsync(
            seed,
            "SELECT id FROM signals WHERE occurred_at >= @from ORDER BY occurred_at DESC",
            ("from", Now.AddDays(-180)));

        plan.ShouldContain("idx_signals_window");
    }

    [RequiresDockerFact]
    public async Task The_per_job_weight_lookup_is_covered_by_its_index()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var signals = await SeedSignalsAsync(seed, count: 3);
        var modelId = Guid.CreateVersion7();
        var weight = new PreferenceWeight(
            Guid.CreateVersion7(), modelId, Dimension.Country, "DE", -0.4m, signals, 0.08m, Now);
        var repo = new PreferenceModelRepository(seed.Database.CreateContext());
        repo.Add(new PreferenceModel(modelId, 1, 250, [weight], Now));
        await repo.SaveChangesAsync();

        var plan = await ExplainAsync(
            seed,
            "SELECT weight FROM preference_weights WHERE model_id = @model AND dimension = 'Country' AND value = 'DE' AND NOT disabled",
            ("model", modelId));

        plan.ShouldContain("idx_preference_weights_lookup");
    }

    private static PreferenceModel NewActiveModel(int version)
    {
        var model = new PreferenceModel(Guid.CreateVersion7(), version, signalCount: 250, [], Now);
        model.Activate(Now);
        return model;
    }

    private static async Task<string> ExplainAsync(
        Seed seed,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(seed.Database.ConnectionString);
        await connection.OpenAsync();

        await using (var noSeq = connection.CreateCommand())
        {
            // Force an index path so the planner cannot fall back to a seq scan on a near-empty table.
            noSeq.CommandText = "SET enable_seqscan = off;";
            await noSeq.ExecuteNonQueryAsync();
        }

        await using var explain = connection.CreateCommand();
        explain.CommandText = "EXPLAIN " + sql;
        foreach (var (name, value) in parameters)
        {
            explain.Parameters.AddWithValue(name, value);
        }

        var plan = new StringBuilder();
        await using var reader = await explain.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            plan.AppendLine(reader.GetString(0));
        }

        return plan.ToString();
    }

    private static async Task<IReadOnlyList<Guid>> SeedSignalsAsync(Seed seed, int count)
    {
        var repo = new SignalRepository(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var id = Guid.CreateVersion7();
            await repo.TryCaptureAsync(Signal.Capture(
                id, seed.JobId, null, SignalKind.Opened, Facts(), Now.AddMinutes(i), SignalWeights.Default));
            ids.Add(id);
        }

        return ids;
    }

    private static async Task<Seed> SeedAsync()
    {
        var database = await TestDatabase.CreateAsync();
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
        ctx.Add(new Job(
            jobId, companyId, rawPostingId, Fingerprint.TryCreate(new string('a', 64)).Value,
            fingerprintVersion: 1, "Staff SRE", normalisedTitle: "staff sre", description: "d",
            applyUrl: "https://acme.com/apply/1", LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            RemotePolicy.Hybrid, EmploymentType.FullTime, PostedAtGranularity.Day, firstSeenAt: Now, lastSeenAt: Now));
        await ctx.SaveChangesAsync();

        return new Seed(database, jobId);
    }

    private sealed record Seed(TestDatabase Database, Guid JobId);
}
