using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Preferences;
using JobHunter.Domain.Reporting;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Shouldly;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F5 T11: the read side of <c>/stats</c> (contract §Commands). Delivered counts come from the append-only
/// <c>delivery_log</c> (invariant 8); opened, ignored, saved and applied come from the <c>signals</c> of the
/// matching kinds (F5 writes the card actions, F6 the applied outcome). The load-bearing properties: the
/// window is half-open <c>[from, to)</c> so two adjacent weeks never double-count a boundary row; only the
/// counted signal kinds contribute (a Rated or an Offer is not one of these five); and a row outside the
/// window is excluded. Read-only — Dapper never writes (architecture rule 4). Requires Docker.
/// </summary>
public sealed class WeeklyStatsQueryTests
{
    private static readonly DateTimeOffset FirstSeen = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowFrom = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowTo = new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset InWindow = new(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task Engagement_counts_deliveries_and_each_reaction_kind_in_the_window()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database);
        var jobA = await SeedJobAsync(database, companyId);
        var jobB = await SeedJobAsync(database, companyId);
        await SeedDeliveryAsync(database, runId, jobA, InWindow);
        await SeedDeliveryAsync(database, runId, jobB, InWindow.AddMinutes(1));
        await SeedSignalAsync(database, jobA, SignalKind.Opened, InWindow);
        await SeedSignalAsync(database, jobA, SignalKind.Saved, InWindow.AddMinutes(1));
        await SeedSignalAsync(database, jobB, SignalKind.Ignored, InWindow);
        await SeedSignalAsync(database, jobA, SignalKind.Applied, InWindow.AddMinutes(2));

        var query = new WeeklyStatsQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var engagement = await query.EngagementAsync(WindowFrom, WindowTo);

        engagement.Delivered.ShouldBe(2);
        engagement.Opened.ShouldBe(1);
        engagement.Ignored.ShouldBe(1);
        engagement.Saved.ShouldBe(1);
        engagement.Applied.ShouldBe(1);
    }

    [RequiresDockerFact]
    public async Task Engagement_excludes_rows_outside_the_half_open_window()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database);
        var job = await SeedJobAsync(database, companyId);

        // The window's upper bound is exclusive: a delivery exactly at `to` belongs to the next window.
        await SeedDeliveryAsync(database, runId, job, WindowTo);
        // A signal a day before the window opens is last week's, not this week's.
        await SeedSignalAsync(database, job, SignalKind.Opened, WindowFrom.AddDays(-1));

        var query = new WeeklyStatsQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var engagement = await query.EngagementAsync(WindowFrom, WindowTo);

        engagement.ShouldBe(Domain.Reporting.WeeklyEngagement.Empty);
    }

    [RequiresDockerFact]
    public async Task Engagement_ignores_signal_kinds_stats_does_not_report()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var job = await SeedJobAsync(database, companyId);

        // Rated and Offer are real signals, but /stats reports only delivered/opened/ignored/saved/applied.
        await SeedSignalAsync(database, job, SignalKind.Rated, InWindow);
        await SeedSignalAsync(database, job, SignalKind.Offer, InWindow.AddMinutes(1));

        var query = new WeeklyStatsQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var engagement = await query.EngagementAsync(WindowFrom, WindowTo);

        engagement.Opened.ShouldBe(0);
        engagement.Saved.ShouldBe(0);
        engagement.Applied.ShouldBe(0);
        engagement.Ignored.ShouldBe(0);
    }

    private static async Task SeedDeliveryAsync(
        TestDatabase database, Guid runId, Guid jobId, DateTimeOffset at)
    {
        var record = new DeliveryRecord(
            Guid.CreateVersion7(), runId, chatId: 4242, CardKey.For(runId, jobId), telegramMessageId: 1, at);
        var log = new DeliveryLog(new NpgsqlConnectionFactory(database.ConnectionString));
        await log.TryRecordAsync(record);
    }

    private static async Task SeedSignalAsync(
        TestDatabase database, Guid jobId, SignalKind kind, DateTimeOffset at)
    {
        var facts = JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
        {
            [Dimension.RemotePolicy] = ["Remote"],
        });
        // Outcome signals (Applied, Offer, …) must reference the application they came from; card actions must not.
        var applicationId = kind is SignalKind.Applied or SignalKind.Interview or SignalKind.Offer or SignalKind.Rejected
            ? Guid.CreateVersion7()
            : (Guid?)null;
        var signal = new Signal(Guid.CreateVersion7(), jobId, applicationId, kind, weight: 1m, facts, at);
        var repo = new SignalRepository(new NpgsqlConnectionFactory(database.ConnectionString));
        await repo.TryCaptureAsync(signal);
    }

    private static async Task<Guid> SeedCompanyAsync(TestDatabase database)
    {
        var companyId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        ctx.Add(new Company(
            companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, FirstSeen));
        await ctx.SaveChangesAsync();
        return companyId;
    }

    private static async Task<Guid> SeedRunAsync(TestDatabase database)
    {
        var runId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        var run = new Run(runId, WindowFrom.AddDays(-1), WindowFrom, ceilingUsd: 5m, WindowFrom);
        run.Abort("seeded", WindowFrom.AddMinutes(1), costBreach: false);
        ctx.Add(run);
        await ctx.SaveChangesAsync();
        return runId;
    }

    private static async Task<Guid> SeedJobAsync(TestDatabase database, Guid companyId)
    {
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var rawPostingId = Guid.CreateVersion7();
        var jobId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, $"acme-{jobId:N}", BindingConfidence.TryCreate(0.9m).Value, "{}", FirstSeen));
        ctx.Add(new JobSource(sourceId, companyId, bindingId, $"https://boards-api.greenhouse.io/v1/boards/acme-{jobId:N}/jobs"));
        ctx.Add(new RawPosting(rawPostingId, sourceId, $"job-{jobId:N}", ContentHash.Compute($"{{\"t\":\"{jobId:N}\"}}"), "{\"t\":\"x\"}", 200, FirstSeen));
        ctx.Add(new Job(
            jobId, companyId, rawPostingId, Fingerprint.TryCreate(jobId.ToString("N") + Guid.NewGuid().ToString("N")).Value,
            fingerprintVersion: 1, "Staff SRE", normalisedTitle: "staff sre", description: "We keep the lights on.",
            applyUrl: $"https://acme.com/apply/{jobId:N}",
            LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            RemotePolicy.Remote, EmploymentType.FullTime, PostedAtGranularity.Day,
            firstSeenAt: FirstSeen, lastSeenAt: FirstSeen, salary: null, status: JobStatus.Live));
        await ctx.SaveChangesAsync();
        return jobId;
    }
}
