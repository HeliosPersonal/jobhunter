using JobHunter.Domain.Applications;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Npgsql;
using Shouldly;
using System.Text;
using Xunit;
using App = JobHunter.Domain.Applications.Application;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// T04: the two read sides of F6 — the pipeline view and the single-application history. The pipeline groups
/// non-archived applications by status, most recently active first (AC-01), each carrying the card display
/// fields and a <c>daysInStage</c> computed at read time rather than stored (contract §Pipeline response).
/// The history view lists every transition with its time and source, in order, and its notes (AC-03), and is
/// retrievable by id even for an archived application. Both are Dapper and read-only — architecture rule 4
/// forbids a write in the Queries namespace. The pipeline scan is covered by <c>idx_applications_pipeline</c>,
/// confirmed by a query plan. Requires Docker.
/// </summary>
public sealed class ApplicationPipelineQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 6, 0, 0, TimeSpan.Zero);
    private static readonly ReminderPolicy Policy = ReminderPolicy.Default;

    [RequiresDockerFact]
    public async Task Pipeline_groups_by_status_most_recently_active_first()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        // Two Saved applications with different activity times, and one Applied — three jobs, three apps.
        var jobA = await SeedJobAsync(seed.Database, seed.CompanyId, "Staff SRE");
        var jobB = await SeedJobAsync(seed.Database, seed.CompanyId, "Platform Engineer");
        var applied = seed.JobId;

        await PersistApplicationAsync(seed.Database, jobA, [(ApplicationStatus.Saved, Now.AddDays(-2))]);
        await PersistApplicationAsync(seed.Database, jobB, [(ApplicationStatus.Saved, Now.AddDays(-1))]);
        var appliedId = await PersistApplicationAsync(
            seed.Database, applied, [(ApplicationStatus.Applied, Now.AddDays(-5))]);
        await SeedScoreAsync(seed.Database, applied, finalScore: 91m);

        var query = new ApplicationPipelineQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var pipeline = await query.PipelineAsync(Now);

        var saved = pipeline.Groups.Single(g => g.Status == ApplicationStatus.Saved);
        // Newest activity first within the group: jobB (yesterday) before jobA (two days ago).
        saved.Applications.Select(a => a.JobId).ShouldBe([jobB, jobA]);
        saved.Applications.Count.ShouldBe(2);

        var appliedGroup = pipeline.Groups.Single(g => g.Status == ApplicationStatus.Applied);
        var entry = appliedGroup.Applications.ShouldHaveSingleItem();
        entry.Id.ShouldBe(appliedId);
        entry.JobId.ShouldBe(applied);
        entry.Title.ShouldBe("Staff SRE");
        entry.Company.ShouldBe("Acme");
        entry.Score.ShouldBe(91m);
        entry.AppliedAt.ShouldBe(Now.AddDays(-5));
        // daysInStage is computed from when the current stage was entered (five days ago), not stored.
        entry.DaysInStage.ShouldBe(5);
    }

    [RequiresDockerFact]
    public async Task Pipeline_days_in_stage_ignores_a_posting_closure()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        // Entered Applied five days ago; the posting then closed yesterday. The closure is a System
        // self-transition (T05) — it must not reset the stage clock, which measures time in the *status*.
        await PersistApplicationAsync(
            seed.Database,
            seed.JobId,
            [(ApplicationStatus.Applied, Now.AddDays(-5))],
            closedAt: Now.AddDays(-1));

        var query = new ApplicationPipelineQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var pipeline = await query.PipelineAsync(Now);

        var entry = pipeline.Groups.Single(g => g.Status == ApplicationStatus.Applied).Applications.ShouldHaveSingleItem();
        entry.PostingClosed.ShouldBeTrue();
        entry.DaysInStage.ShouldBe(5);
    }

    [RequiresDockerFact]
    public async Task Pipeline_excludes_archived_applications()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        await PersistApplicationAsync(
            seed.Database, seed.JobId, [(ApplicationStatus.Applied, Now.AddDays(-1))], archived: true);

        var query = new ApplicationPipelineQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var pipeline = await query.PipelineAsync(Now);

        // An archived application is retained in full but never shown in the pipeline (SAD §8 Archival).
        pipeline.Groups.ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task History_lists_every_transition_in_order_with_its_notes()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var applicationId = await PersistApplicationAsync(
            seed.Database,
            seed.JobId,
            [
                (ApplicationStatus.Saved, Now.AddDays(-4)),
                (ApplicationStatus.Applied, Now.AddDays(-3)),
                (ApplicationStatus.Interview, Now.AddDays(-1)),
            ],
            note: ("Recruiter call went well.", Now.AddHours(-2)));

        var query = new ApplicationHistoryQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var history = await query.HistoryAsync(applicationId);

        history.ShouldNotBeNull();
        history.JobId.ShouldBe(seed.JobId);
        history.Title.ShouldBe("Staff SRE");
        history.Company.ShouldBe("Acme");
        history.Status.ShouldBe(ApplicationStatus.Interview);
        // The creating New transition plus the three moves, oldest first (AC-03).
        history.Transitions.Select(t => t.To).ShouldBe(
        [
            ApplicationStatus.New,
            ApplicationStatus.Saved,
            ApplicationStatus.Applied,
            ApplicationStatus.Interview,
        ]);
        history.Transitions[0].From.ShouldBeNull();
        history.Transitions[1].From.ShouldBe(ApplicationStatus.New);
        history.Transitions.ShouldAllBe(t => t.Source == TransitionSource.Telegram);
        history.Notes.ShouldHaveSingleItem().Body.ShouldBe("Recruiter call went well.");
    }

    [RequiresDockerFact]
    public async Task History_retrieves_an_archived_application_by_id()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var applicationId = await PersistApplicationAsync(
            seed.Database, seed.JobId, [(ApplicationStatus.Rejected, Now.AddDays(-1))], archived: true);

        var query = new ApplicationHistoryQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var history = await query.HistoryAsync(applicationId);

        // Hidden from the pipeline, but the full record is still retrievable by id.
        history.ShouldNotBeNull();
        history.Archived.ShouldBeTrue();
        history.Status.ShouldBe(ApplicationStatus.Rejected);
    }

    [RequiresDockerFact]
    public async Task History_returns_null_for_an_unknown_application()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var query = new ApplicationHistoryQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));

        (await query.HistoryAsync(Guid.CreateVersion7())).ShouldBeNull();
    }

    [RequiresDockerFact]
    public async Task Due_reminders_returns_only_non_archived_applications_past_their_next_action()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        // Applied 11 days ago: the 10-day threshold has passed, so next_action_at is in the past — due.
        var dueJob = seed.JobId;
        var dueId = await PersistApplicationAsync(seed.Database, dueJob, [(ApplicationStatus.Applied, Now.AddDays(-11))]);

        // Applied yesterday: next_action_at is nine days out — not due.
        var freshJob = await SeedJobAsync(seed.Database, seed.CompanyId, "Platform Engineer");
        await PersistApplicationAsync(seed.Database, freshJob, [(ApplicationStatus.Applied, Now.AddDays(-1))]);

        // Rejected then archived: terminal has no threshold (next_action_at is null) and archived is hidden anyway.
        var archivedJob = await SeedJobAsync(seed.Database, seed.CompanyId, "Backend Engineer");
        await PersistApplicationAsync(
            seed.Database, archivedJob,
            [(ApplicationStatus.Applied, Now.AddDays(-20)), (ApplicationStatus.Rejected, Now.AddDays(-19))],
            archived: true);

        var query = new DueReminderQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var due = await query.DueAsync(Now);

        var reminder = due.ShouldHaveSingleItem();
        reminder.ApplicationId.ShouldBe(dueId);
        reminder.JobId.ShouldBe(dueJob);
        reminder.Title.ShouldBe("Staff SRE");
        reminder.Company.ShouldBe("Acme");
        reminder.ApplyUrl.ShouldNotBeNullOrWhiteSpace();
        reminder.Status.ShouldBe(ApplicationStatus.Applied);
        reminder.PostingClosed.ShouldBeFalse();
        reminder.LastReminderCondition.ShouldBeNull();
        reminder.CurrentCondition().ShouldBe("stale:Applied");
        reminder.IsAlreadyReminded().ShouldBeFalse();
    }

    [RequiresDockerFact]
    public async Task Due_reminders_carries_the_last_condition_and_flags_a_closed_posting()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        // A Saved application whose posting has closed and past its 5-day threshold — the "drop or apply
        // elsewhere" nudge (test-plan Saved + closed). A prior reminder for the same closed condition means
        // it is already suppressed.
        await PersistApplicationAsync(
            seed.Database, seed.JobId, [(ApplicationStatus.Saved, Now.AddDays(-6))],
            closedAt: Now.AddDays(-2), remindedCondition: (App.PostingClosedCondition, Now.AddDays(-1)));

        var query = new DueReminderQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var reminder = (await query.DueAsync(Now)).ShouldHaveSingleItem();

        reminder.PostingClosed.ShouldBeTrue();
        reminder.CurrentCondition().ShouldBe(App.PostingClosedCondition);
        reminder.LastReminderCondition.ShouldBe(App.PostingClosedCondition);
        // The last reminder already fired for this exact condition — suppressed until it clears or recurs (QG-3).
        reminder.IsAlreadyReminded().ShouldBeTrue();
    }

    [RequiresDockerFact]
    public async Task The_due_reminder_query_is_covered_by_idx_applications_due()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        await PersistApplicationAsync(seed.Database, seed.JobId, [(ApplicationStatus.Applied, Now.AddDays(-11))]);

        var plan = await ExplainAsync(seed.Database, DueReminderQuery.Sql, ("now", Now));

        plan.ShouldNotContain("Seq Scan");
        plan.ShouldContain("idx_applications_due");
    }

    [RequiresDockerFact]
    public async Task The_pipeline_query_is_covered_by_idx_applications_pipeline()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        await PersistApplicationAsync(seed.Database, seed.JobId, [(ApplicationStatus.Applied, Now.AddDays(-1))]);

        var plan = await ExplainAsync(seed.Database, ApplicationPipelineQuery.Sql);

        plan.ShouldContain("idx_applications_pipeline");
    }

    private static async Task<Guid> PersistApplicationAsync(
        TestDatabase database,
        Guid jobId,
        IReadOnlyList<(ApplicationStatus To, DateTimeOffset At)> moves,
        (string Body, DateTimeOffset At)? note = null,
        bool archived = false,
        DateTimeOffset? closedAt = null,
        (string Condition, DateTimeOffset At)? remindedCondition = null)
    {
        var id = Guid.CreateVersion7();
        var app = App.Create(id, jobId, moves[0].At.AddMinutes(-1), TransitionSource.Telegram);
        foreach (var (to, at) in moves)
        {
            app.ChangeStatus(to, TransitionSource.Telegram, at, Policy);
        }

        if (note is { } n)
        {
            app.AddNote(Guid.CreateVersion7(), n.Body, n.At);
        }

        if (closedAt is { } c)
        {
            app.MarkPostingClosed(c);
        }

        await using var ctx = database.CreateContext();
        ctx.Add(app);
        if (archived)
        {
            // Archival is an internal state the sweep sets; set it through EF for the read-side test.
            ctx.Entry(app).Property(nameof(App.Archived)).CurrentValue = true;
        }

        if (remindedCondition is { } r)
        {
            // Seed a prior reminder without pushing next_action_at forward, so the row stays due while its
            // last-reminder condition marks it already-reminded (the suppression case).
            ctx.Entry(app).Property(nameof(App.LastReminderCondition)).CurrentValue = r.Condition;
            ctx.Entry(app).Property(nameof(App.LastReminderAt)).CurrentValue = r.At;
        }

        await ctx.SaveChangesAsync();
        return id;
    }

    private static async Task<string> ExplainAsync(
        TestDatabase database, string query, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();

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

    private static async Task SeedScoreAsync(TestDatabase database, Guid jobId, decimal finalScore)
    {
        var runId = Guid.CreateVersion7();
        await using (var ctx = database.CreateContext())
        {
            var run = new Run(runId, Now.AddDays(-1), Now, ceilingUsd: 5m, Now);
            run.Abort("seeded", Now.AddMinutes(1), costBreach: false);
            ctx.Add(run);
            await ctx.SaveChangesAsync();
        }

        var fraction = finalScore / 100m;
        var components = new ScoreComponents(
            match: fraction, alignment: fraction, preference: fraction, freshness: fraction,
            confidenceMultiplier: 1.00m);
        var score = new Score(
            jobId, runId, finalScore, components, RankingWeights.Default, preferenceModelId: null,
            suppressed: false, suppressionReason: null, Now);

        var repo = new ScoreRepository(
            database.CreateContext(), new NpgsqlConnectionFactory(database.ConnectionString));
        await repo.UpsertAsync(score);
    }

    private static async Task<Seed> SeedAsync()
    {
        var database = await TestDatabase.CreateAsync();
        var companyId = Guid.CreateVersion7();

        await using (var ctx = database.CreateContext())
        {
            ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now));
            await ctx.SaveChangesAsync();
        }

        var jobId = await SeedJobAsync(database, companyId, "Staff SRE");
        return new Seed(database, companyId, jobId);
    }

    private static async Task<Guid> SeedJobAsync(TestDatabase database, Guid companyId, string title)
    {
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var rawPostingId = Guid.CreateVersion7();
        var jobId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, $"acme-{jobId:N}", BindingConfidence.TryCreate(0.9m).Value, "{}", Now));
        ctx.Add(new JobSource(sourceId, companyId, bindingId, $"https://boards-api.greenhouse.io/v1/boards/acme-{jobId:N}/jobs"));
        ctx.Add(new RawPosting(rawPostingId, sourceId, $"job-{jobId:N}", ContentHash.Compute($"{{\"t\":\"{jobId:N}\"}}"), "{\"t\":\"x\"}", 200, Now));
        ctx.Add(new Job(
            jobId, companyId, rawPostingId, Fingerprint.TryCreate(jobId.ToString("N") + Guid.NewGuid().ToString("N")).Value,
            fingerprintVersion: 1, title, normalisedTitle: title.ToLowerInvariant(), description: "d",
            applyUrl: $"https://acme.com/apply/{jobId:N}",
            LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            RemotePolicy.Hybrid, EmploymentType.FullTime, PostedAtGranularity.Day,
            firstSeenAt: Now, lastSeenAt: Now, status: JobStatus.Live));
        await ctx.SaveChangesAsync();
        return jobId;
    }

    private sealed record Seed(TestDatabase Database, Guid CompanyId, Guid JobId);
}
