using JobHunter.Domain.Companies;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Profiles;
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
/// T02: the F4 persistence. The migration creates <c>profiles</c>, <c>cv_versions</c>, <c>matches</c> and
/// <c>scores</c> with all eight declared indexes; the three partial/unique indexes that carry rules are
/// asserted by violating each — a second active Profile, a second active CV per profile, and a duplicate
/// match key. The digest query is covered by <c>idx_scores_run_final</c>, verified by a query plan.
/// <c>extracted_text</c> exists on exactly one table in the whole schema. Match and Score upserts are
/// idempotent, and the re-staling sweep clears <c>is_current</c> without deleting. Requires Docker.
/// </summary>
public sealed class MatchingPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    private static readonly string[] ExpectedIndexes =
    [
        "uq_profiles_active",
        "uq_cv_versions_active",
        "uq_cv_versions_hash",
        "uq_matches_job_run_profile",
        "idx_matches_current",
        "idx_matches_cv_version",
        "idx_scores_run_final",
        "idx_scores_suppressed",
        "uq_re_match_queue_open",
    ];

    [RequiresDockerFact]
    public async Task All_declared_indexes_exist_after_the_migration()
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
    public async Task Extracted_text_exists_on_exactly_one_table_in_the_whole_schema()
    {
        await using var database = await TestDatabase.CreateAsync();

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT table_name FROM information_schema.columns " +
            "WHERE column_name = 'extracted_text' AND table_schema = 'public'";

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        // The single storage location for CV content in the whole schema (data-model §cv_versions).
        tables.ShouldHaveSingleItem().ShouldBe("cv_versions");
    }

    [RequiresDockerFact]
    public async Task A_second_active_profile_is_rejected_by_the_partial_unique_index()
    {
        await using var database = await TestDatabase.CreateAsync();
        var repo = new ProfileRepository(database.CreateContext());
        repo.Add(NewProfile(isActive: true));
        await repo.SaveChangesAsync();

        // A second active Profile violates uq_profiles_active — the database is the arbiter.
        var second = new ProfileRepository(database.CreateContext());
        second.Add(NewProfile(isActive: true));
        var ex = await Should.ThrowAsync<DbUpdateException>(() => second.SaveChangesAsync());

        var postgres = ex.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe("uq_profiles_active");
    }

    [RequiresDockerFact]
    public async Task A_second_inactive_profile_is_allowed()
    {
        await using var database = await TestDatabase.CreateAsync();
        var repo = new ProfileRepository(database.CreateContext());
        repo.Add(NewProfile(isActive: true));
        await repo.SaveChangesAsync();

        // Only one active is constrained; any number of inactive Profiles may exist.
        var second = new ProfileRepository(database.CreateContext());
        second.Add(NewProfile(isActive: false));
        await second.SaveChangesAsync();

        await using var read = database.CreateContext();
        (await read.Set<Profile>().CountAsync()).ShouldBe(2);
    }

    [RequiresDockerFact]
    public async Task A_second_active_cv_for_a_profile_is_rejected_by_the_partial_unique_index()
    {
        await using var database = await TestDatabase.CreateAsync();
        var profile = NewProfile(isActive: true);
        var profiles = new ProfileRepository(database.CreateContext());
        profiles.Add(profile);
        await profiles.SaveChangesAsync();

        var cvs = new CvVersionRepository(database.CreateContext());
        cvs.Add(NewCv(profile.Id, version: 1, isActive: true, hash: new string('a', 64)));
        await cvs.SaveChangesAsync();

        // A second active CV for the same profile violates uq_cv_versions_active.
        var second = new CvVersionRepository(database.CreateContext());
        second.Add(NewCv(profile.Id, version: 2, isActive: true, hash: new string('b', 64)));
        var ex = await Should.ThrowAsync<DbUpdateException>(() => second.SaveChangesAsync());

        var postgres = ex.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe("uq_cv_versions_active");
    }

    [RequiresDockerFact]
    public async Task Re_uploading_identical_content_is_rejected_by_the_hash_index()
    {
        await using var database = await TestDatabase.CreateAsync();
        var profile = NewProfile(isActive: true);
        var profiles = new ProfileRepository(database.CreateContext());
        profiles.Add(profile);
        await profiles.SaveChangesAsync();

        var hash = new string('c', 64);
        var cvs = new CvVersionRepository(database.CreateContext());
        cvs.Add(NewCv(profile.Id, version: 1, isActive: false, hash: hash));
        await cvs.SaveChangesAsync();

        var second = new CvVersionRepository(database.CreateContext());
        second.Add(NewCv(profile.Id, version: 2, isActive: false, hash: hash));
        var ex = await Should.ThrowAsync<DbUpdateException>(() => second.SaveChangesAsync());

        var postgres = ex.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe("uq_cv_versions_hash");
    }

    [RequiresDockerFact]
    public async Task Match_upsert_round_trips_and_is_idempotent_on_the_job_run_profile_key()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = new MatchRepository(seed.Database.CreateContext(), new NpgsqlConnectionFactory(seed.Database.ConnectionString));

        (await repo.UpsertAsync(seed.NewMatch())).ShouldBeTrue();
        // A replay of the same (job_id, run_id, profile_id) — a different aggregate id, same key — is a no-op.
        (await repo.UpsertAsync(seed.NewMatch())).ShouldBeFalse();

        await using var read = seed.Database.CreateContext();
        var stored = await read.Set<Match>().SingleAsync();
        stored.JobId.ShouldBe(seed.JobId);
        stored.MatchScore.ShouldBe(82);
        stored.InterviewProbability.ShouldBe(InterviewProbability.Good);
        stored.SalaryExpectation!.Currency.ShouldBe("EUR");
        stored.MissingSkills.ShouldBe(["Rust"]);
        stored.Reasons.ShouldHaveSingleItem();
        stored.IsCurrent.ShouldBeTrue();
    }

    [RequiresDockerFact]
    public async Task The_re_staling_sweep_clears_is_current_without_deleting()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = new MatchRepository(seed.Database.CreateContext(), new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        await repo.UpsertAsync(seed.NewMatch());

        var marked = await repo.MarkNotCurrentForCvVersionAsync(seed.CvVersionId);
        marked.ShouldBe(1);

        await using var read = seed.Database.CreateContext();
        var stored = await read.Set<Match>().SingleAsync();
        stored.IsCurrent.ShouldBeFalse();
    }

    [RequiresDockerFact]
    public async Task The_activation_re_staling_sweep_clears_only_other_versions_and_deletes_nothing()
    {
        // T09/AC-08: activating a new version stales every current match NOT of that version — never the
        // active one's own, and never by deletion. The row survives with is_current = false.
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = new MatchRepository(seed.Database.CreateContext(), new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        await repo.UpsertAsync(seed.NewMatch());

        // The just-activated version is a different one from the match's; the match must be staled.
        var staled = await repo.MarkNotCurrentExceptCvVersionAsync(Guid.CreateVersion7());
        staled.ShouldBe(1);

        await using var read = seed.Database.CreateContext();
        var stored = await read.Set<Match>().SingleAsync();
        stored.IsCurrent.ShouldBeFalse();

        // A second sweep against the match's own version leaves it untouched (nothing to stale).
        var noop = await repo.MarkNotCurrentExceptCvVersionAsync(seed.CvVersionId);
        noop.ShouldBe(0);
    }

    // ---- T09: the re-match backlog repository -----------------------------------------------------

    [RequiresDockerFact]
    public async Task Enqueue_round_trips_and_is_idempotent_per_open_job()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = new ReMatchBacklogRepository(new NpgsqlConnectionFactory(seed.Database.ConnectionString));

        (await repo.EnqueueAsync(seed.NewReMatchItem())).ShouldBeTrue();
        // A second open request for the same job is the idempotent no-op the partial unique index enforces.
        (await repo.EnqueueAsync(seed.NewReMatchItem())).ShouldBeFalse();

        (await repo.PendingJobIdsAsync()).ShouldBe([seed.JobId]);
    }

    [RequiresDockerFact]
    public async Task Consuming_a_job_drains_it_and_allows_a_fresh_enqueue()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = new ReMatchBacklogRepository(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        await repo.EnqueueAsync(seed.NewReMatchItem());

        (await repo.MarkConsumedAsync([seed.JobId])).ShouldBe(1);
        (await repo.PendingJobIdsAsync()).ShouldBeEmpty();

        // Once drained, the partial index no longer collides, so a later CV change can queue the job again.
        (await repo.EnqueueAsync(seed.NewReMatchItem())).ShouldBeTrue();
        (await repo.PendingJobIdsAsync()).ShouldBe([seed.JobId]);

        // The consumed row survives — the backlog is history, not a delete queue.
        await using var read = seed.Database.CreateContext();
        (await read.Set<ReMatchQueueItem>().CountAsync(x => x.Consumed)).ShouldBe(1);
    }

    [RequiresDockerFact]
    public async Task Marking_an_empty_set_consumed_is_a_no_op()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = new ReMatchBacklogRepository(new NpgsqlConnectionFactory(seed.Database.ConnectionString));

        (await repo.MarkConsumedAsync([])).ShouldBe(0);
    }

    [RequiresDockerFact]
    public async Task Score_upsert_round_trips_and_is_idempotent_on_the_job_run_key()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = new ScoreRepository(seed.Database.CreateContext(), new NpgsqlConnectionFactory(seed.Database.ConnectionString));

        (await repo.UpsertAsync(seed.NewScore())).ShouldBeTrue();
        (await repo.UpsertAsync(seed.NewScore())).ShouldBeFalse();

        await using var read = seed.Database.CreateContext();
        var stored = await read.Set<Score>().SingleAsync();
        stored.JobId.ShouldBe(seed.JobId);
        stored.RunId.ShouldBe(seed.RunId);
        // Every stored component round-trips, including the alignment component (T14).
        stored.Components.Match.ShouldBe(0.80m);
        stored.Components.Alignment.ShouldBe(0.60m);
        stored.Components.Preference.ShouldBe(0.50m);
        stored.Components.Freshness.ShouldBe(0.40m);
        // The stored total reconciles from the stored components (QG-1).
        stored.Components.Reconcile(RankingWeights.Default).ShouldBe(stored.FinalScore, tolerance: 0.01m);
    }

    [RequiresDockerFact]
    public async Task A_suppressed_score_can_exist_with_no_matching_match_row()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = new ScoreRepository(seed.Database.CreateContext(), new NpgsqlConnectionFactory(seed.Database.ConnectionString));

        // A pre-match exclusion: a scores row with no matches row, suppressed and reasoned.
        var excluded = new Score(
            seed.JobId, seed.RunId, 0m, new ScoreComponents(0m, 0m, 0m, 0m, 1.00m), RankingWeights.Default,
            preferenceModelId: null, suppressed: true,
            suppressionReason: "Excluded before matching: employment type not accepted.", Now);
        (await repo.UpsertAsync(excluded)).ShouldBeTrue();

        await using var read = seed.Database.CreateContext();
        (await read.Set<Match>().CountAsync()).ShouldBe(0);
        (await read.Set<Score>().SingleAsync()).Suppressed.ShouldBeTrue();
    }

    [RequiresDockerFact]
    public async Task The_digest_query_is_covered_by_idx_scores_run_final()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = new ScoreRepository(seed.Database.CreateContext(), new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        await repo.UpsertAsync(seed.NewScore());

        await using var connection = new NpgsqlConnection(seed.Database.ConnectionString);
        await connection.OpenAsync();

        // Force an index scan so the planner cannot fall back to a seq scan on a near-empty table.
        await using (var noSeq = connection.CreateCommand())
        {
            noSeq.CommandText = "SET enable_seqscan = off;";
            await noSeq.ExecuteNonQueryAsync();
        }

        await using var explain = connection.CreateCommand();
        explain.CommandText =
            "EXPLAIN SELECT job_id FROM scores WHERE run_id = @run AND NOT suppressed ORDER BY final_score DESC";
        explain.Parameters.AddWithValue("run", seed.RunId);

        var plan = new System.Text.StringBuilder();
        await using var reader = await explain.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            plan.AppendLine(reader.GetString(0));
        }

        plan.ToString().ShouldContain("idx_scores_run_final");
    }

    private static Profile NewProfile(bool isActive) =>
        new(
            Guid.CreateVersion7(), isActive, "Owner",
            salaryFloor: null, salaryFloorCurrency: null, TimezoneBand.EMEA,
            preferredCountries: ["Germany"], employmentTypes: [EmploymentType.FullTime], Now);

    private static CvVersion NewCv(Guid profileId, short version, bool isActive, string hash) =>
        new(
            Guid.CreateVersion7(), profileId, version, isActive, "cv.pdf", "application/pdf",
            sizeBytes: 1024, contentHash: hash, extractedText: "Extracted CV text.",
            uploadedAt: Now, activatedAt: isActive ? Now : null);

    private static async Task<Seed> SeedAsync()
    {
        var database = await TestDatabase.CreateAsync();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var rawPostingId = Guid.CreateVersion7();
        var jobId = Guid.CreateVersion7();
        var runId = Guid.CreateVersion7();
        var profileId = Guid.CreateVersion7();
        var cvVersionId = Guid.CreateVersion7();

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
        ctx.Add(new Profile(
            profileId, isActive: true, "Owner", salaryFloor: null, salaryFloorCurrency: null,
            TimezoneBand.EMEA, preferredCountries: ["Germany"], employmentTypes: [EmploymentType.FullTime], Now));
        ctx.Add(new CvVersion(
            cvVersionId, profileId, version: 1, isActive: true, "cv.pdf", "application/pdf",
            sizeBytes: 1024, contentHash: new string('a', 64), extractedText: "Extracted CV text.",
            uploadedAt: Now, activatedAt: Now));
        await ctx.SaveChangesAsync();

        return new Seed(database, jobId, runId, profileId, cvVersionId);
    }

    private sealed record Seed(TestDatabase Database, Guid JobId, Guid RunId, Guid ProfileId, Guid CvVersionId)
    {
        public Match NewMatch() =>
            new(
                Guid.CreateVersion7(), JobId, RunId, ProfileId, CvVersionId,
                matchScore: 82, InterviewProbability.Good, missingSkills: ["Rust"],
                SalaryExpectation.TryCreate(100000m, 140000m, "EUR").Value,
                reasons: ["Strong platform-engineering overlap."], promptVersion: "match-v1", Now);

        public Score NewScore()
        {
            var components = new ScoreComponents(0.80m, 0.60m, 0.50m, 0.40m, 1.00m);
            var final = components.Reconcile(RankingWeights.Default);
            return new Score(
                JobId, RunId, final, components, RankingWeights.Default,
                preferenceModelId: null, suppressed: false, suppressionReason: null, Now);
        }

        public ReMatchQueueItem NewReMatchItem() =>
            new(Guid.CreateVersion7(), JobId, CvVersionId, Now);
    }
}
