using System.Text;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Npgsql;
using Shouldly;
using Xunit;
using App = JobHunter.Domain.Applications.Application;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// T02: the F6 persistence. The migration creates <c>applications</c>, <c>application_transitions</c> and
/// <c>application_notes</c> with all six declared indexes. Two rules are proven against the database, not
/// the repository: <c>uq_applications_job</c> enforces one application per job (asserted by violating it),
/// and the transitions repository exposes no update and no delete path — a correction is a new row, which is
/// what makes the history trustworthy (QG-1). The reminder sweep is covered by <c>idx_applications_due</c>
/// and the pipeline view by <c>idx_applications_pipeline</c>, both verified with a query plan. Requires Docker.
/// </summary>
public sealed class ApplicationPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 6, 0, 0, TimeSpan.Zero);
    private static readonly ReminderPolicy Policy = ReminderPolicy.Default;

    private static readonly string[] ExpectedIndexes =
    [
        "uq_applications_job",
        "idx_applications_pipeline",
        "idx_applications_due",
        "idx_transitions_application",
        "idx_transitions_outcome",
        "idx_notes_application",
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
    public async Task An_application_with_its_transitions_and_notes_round_trips()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var app = App.Create(Guid.CreateVersion7(), seed.JobId, Now, TransitionSource.Telegram);
        app.ChangeStatus(ApplicationStatus.Applied, TransitionSource.Telegram, Now.AddMinutes(1), Policy);
        app.AddNote(Guid.CreateVersion7(), "Recruiter replied within the hour.", Now.AddMinutes(2));

        var repo = new ApplicationRepository(seed.Database.CreateContext());
        repo.Add(app);
        await repo.SaveChangesAsync();

        var read = new ApplicationRepository(seed.Database.CreateContext());
        var loaded = await read.FindByJobAsync(seed.JobId);

        loaded.ShouldNotBeNull();
        loaded.JobId.ShouldBe(seed.JobId);
        loaded.Status.ShouldBe(ApplicationStatus.Applied);
        loaded.AppliedAt.ShouldBe(Now.AddMinutes(1));
        loaded.NextActionAt.ShouldBe(Now.AddMinutes(1).Add(Policy.ThresholdFor(ApplicationStatus.Applied)!.Value));
        // The creating (from null → New) and the New → Applied transition, in order.
        loaded.Transitions.Select(t => t.To).ShouldBe([ApplicationStatus.New, ApplicationStatus.Applied]);
        loaded.Transitions[0].From.ShouldBeNull();
        loaded.Transitions[1].From.ShouldBe(ApplicationStatus.New);
        loaded.Notes.ShouldHaveSingleItem().Body.ShouldBe("Recruiter replied within the hour.");
    }

    [RequiresDockerFact]
    public async Task A_second_application_for_the_same_job_is_rejected_by_uq_applications_job()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var first = new ApplicationRepository(seed.Database.CreateContext());
        first.Add(App.Create(Guid.CreateVersion7(), seed.JobId, Now, TransitionSource.Telegram));
        await first.SaveChangesAsync();

        // One application per job (uq_applications_job) — a second for the same job fails at commit.
        var second = new ApplicationRepository(seed.Database.CreateContext());
        second.Add(App.Create(Guid.CreateVersion7(), seed.JobId, Now, TransitionSource.Telegram));
        var ex = await Should.ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(
            () => second.SaveChangesAsync());

        var postgres = ex.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe("uq_applications_job");
    }

    [RequiresDockerFact]
    public void The_application_repository_exposes_no_update_and_no_delete_path()
    {
        // QG-1 as an API property: the history is append-only, so the port offers a stage, a read and a
        // commit — never a remove or an edit of a recorded transition. A correction is a new transition,
        // appended through the aggregate, not a mutation of the log.
        typeof(IApplicationRepository).GetMethods()
            .Select(m => m.Name)
            .ShouldBe(["Add", "FindByJobAsync", "SaveChangesAsync"], ignoreOrder: true);

        typeof(ApplicationRepository).GetMethods()
            .Where(m => m.DeclaringType == typeof(ApplicationRepository))
            .Select(m => m.Name)
            .ShouldBe(["Add", "FindByJobAsync", "SaveChangesAsync"], ignoreOrder: true);
    }

    [RequiresDockerFact]
    public async Task The_reminder_sweep_query_is_covered_by_idx_applications_due()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        await PersistAppliedApplicationAsync(seed);

        var plan = await ExplainAsync(
            seed,
            "SELECT id FROM applications " +
            "WHERE next_action_at IS NOT NULL AND NOT archived AND next_action_at <= @now",
            ("now", Now.AddDays(30)));

        plan.ShouldNotContain("Seq Scan");
        plan.ShouldContain("idx_applications_due");
    }

    [RequiresDockerFact]
    public async Task The_pipeline_query_is_covered_by_idx_applications_pipeline()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        await PersistAppliedApplicationAsync(seed);

        var plan = await ExplainAsync(
            seed,
            "SELECT id FROM applications " +
            "WHERE NOT archived AND status = @status ORDER BY last_activity_at DESC",
            ("status", nameof(ApplicationStatus.Applied)));

        plan.ShouldNotContain("Seq Scan");
        plan.ShouldContain("idx_applications_pipeline");
    }

    private static async Task PersistAppliedApplicationAsync(Seed seed)
    {
        var app = App.Create(Guid.CreateVersion7(), seed.JobId, Now, TransitionSource.Telegram);
        app.ChangeStatus(ApplicationStatus.Applied, TransitionSource.Telegram, Now.AddMinutes(1), Policy);
        var repo = new ApplicationRepository(seed.Database.CreateContext());
        repo.Add(app);
        await repo.SaveChangesAsync();
    }

    private static async Task<string> ExplainAsync(Seed seed, string query, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(seed.Database.ConnectionString);
        await connection.OpenAsync();

        // Force an index path so the planner cannot fall back to a seq scan on a near-empty table.
        await using (var noSeq = connection.CreateCommand())
        {
            noSeq.CommandText = "SET enable_seqscan = off;";
            await noSeq.ExecuteNonQueryAsync();
        }

        await using var explain = connection.CreateCommand();
        explain.CommandText = "EXPLAIN " + query;
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
