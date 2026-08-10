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
/// F4 T20 done-when 3, D5: the weekly ratings-based <c>precision@10</c> read model. It anchors on the latest
/// opened rating round, measures over that week's top-ten delivered cards, and counts how many carry a
/// <c>Rated</c> signal — a "worth opening" tap. A never-rated system reports null (not a misleading zero); a
/// round that delivered nothing reports a measured zero; only the delivered top-ten count, capped at ten. It
/// selects nothing about the Owner's CV. Requires Docker.
/// </summary>
public sealed class WeeklyPrecisionQueryTests
{
    private static readonly DateTimeOffset FirstSeen = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RunStart = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    // The week under review: [WeekStart, WeekStart + 7d). Deliveries land seven hours in.
    private static readonly DateTimeOffset WeekStart = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DeliveredAt = WeekStart.AddHours(7);
    private const long OwnerChat = 4242;

    // One generator across the test so two opened rounds get distinct primary keys, not two id #1s.
    private readonly SequentialIdGenerator _ids = new();

    [RequiresDockerFact]
    public async Task It_measures_rated_cards_over_the_weeks_delivered_top_ten()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);

        var top = await SeedJobAsync(database, companyId);
        var second = await SeedJobAsync(database, companyId);
        var third = await SeedJobAsync(database, companyId);
        var fourth = await SeedJobAsync(database, companyId);
        await SeedDigestAsync(database, runId, [(top, 1), (second, 2), (third, 3), (fourth, 4)]);
        foreach (var job in new[] { top, second, third, fourth })
        {
            await SeedDeliveryAsync(database, runId, job, DeliveredAt);
        }

        // Two of the four delivered cards were rated "worth opening": precision 2/4 = 0.5.
        await SeedRatedAsync(database, top);
        await SeedRatedAsync(database, third);
        await OpenRoundAsync(database, WeekStart);

        var query = new WeeklyPrecisionQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var precision = await query.LatestAsync();

        precision.ShouldNotBeNull();
        precision!.WeekStart.ShouldBe(WeekStart);
        precision.Considered.ShouldBe(4);
        precision.Hits.ShouldBe(2);
        precision.Precision.ShouldBe(0.5m);
    }

    [RequiresDockerFact]
    public async Task A_never_rated_system_reports_null()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;

        var query = new WeeklyPrecisionQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        (await query.LatestAsync()).ShouldBeNull();
    }

    [RequiresDockerFact]
    public async Task A_round_that_delivered_nothing_reports_a_measured_zero()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;

        await OpenRoundAsync(database, WeekStart);

        var query = new WeeklyPrecisionQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var precision = await query.LatestAsync();

        precision.ShouldNotBeNull();
        precision!.Considered.ShouldBe(0);
        precision.Hits.ShouldBe(0);
        precision.Precision.ShouldBe(0m);
    }

    [RequiresDockerFact]
    public async Task An_undelivered_but_rated_card_does_not_count()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);

        var delivered = await SeedJobAsync(database, companyId);
        var undelivered = await SeedJobAsync(database, companyId);
        await SeedDigestAsync(database, runId, [(delivered, 1), (undelivered, 2)]);
        await SeedDeliveryAsync(database, runId, delivered, DeliveredAt);

        // The undelivered card was somehow rated, but only delivered cards form the denominator.
        await SeedRatedAsync(database, delivered);
        await SeedRatedAsync(database, undelivered);
        await OpenRoundAsync(database, WeekStart);

        var query = new WeeklyPrecisionQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var precision = await query.LatestAsync();

        precision!.Considered.ShouldBe(1);
        precision.Hits.ShouldBe(1);
        precision.Precision.ShouldBe(1.0m);
    }

    [RequiresDockerFact]
    public async Task Only_the_top_ten_delivered_cards_are_measured()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);

        // Twelve delivered cards, ranks 1..12. The eleventh and twelfth are rated but fall below the top ten,
        // so they must neither count as hits nor inflate the denominator past ten.
        var ranked = new List<(Guid jobId, int rank)>();
        for (var rank = 1; rank <= 12; rank++)
        {
            ranked.Add((await SeedJobAsync(database, companyId), rank));
        }

        await SeedDigestAsync(database, runId, ranked);
        foreach (var (jobId, rank) in ranked)
        {
            await SeedDeliveryAsync(database, runId, jobId, DeliveredAt);
            if (rank > 10)
            {
                await SeedRatedAsync(database, jobId);
            }
        }

        await OpenRoundAsync(database, WeekStart);

        var query = new WeeklyPrecisionQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var precision = await query.LatestAsync();

        precision!.Considered.ShouldBe(10);
        precision.Hits.ShouldBe(0);
        precision.Precision.ShouldBe(0m);
    }

    [RequiresDockerFact]
    public async Task Only_the_latest_round_is_reported()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);

        // A card delivered and rated in the current week; an older round exists too, but the latest wins.
        var job = await SeedJobAsync(database, companyId);
        await SeedDigestAsync(database, runId, [(job, 1)]);
        await SeedDeliveryAsync(database, runId, job, DeliveredAt);
        await SeedRatedAsync(database, job);

        await OpenRoundAsync(database, WeekStart.AddDays(-7));
        await OpenRoundAsync(database, WeekStart);

        var query = new WeeklyPrecisionQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var precision = await query.LatestAsync();

        precision!.WeekStart.ShouldBe(WeekStart);
        precision.Considered.ShouldBe(1);
        precision.Hits.ShouldBe(1);
    }

    private async Task OpenRoundAsync(TestDatabase database, DateTimeOffset weekStart)
    {
        var log = new RatingRoundLog(new NpgsqlConnectionFactory(database.ConnectionString), _ids);
        (await log.TryOpenAsync(weekStart, OwnerChat, weekStart.AddDays(7))).ShouldBeTrue();
    }

    private static async Task<Guid> SeedCompanyAsync(TestDatabase database)
    {
        var companyId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, RunStart));
        await ctx.SaveChangesAsync();
        return companyId;
    }

    private static async Task<Guid> SeedRunAsync(TestDatabase database, DateTimeOffset startedAt)
    {
        var runId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        var run = new Run(runId, startedAt.AddDays(-1), startedAt, ceilingUsd: 5m, startedAt);
        run.Abort("seeded", startedAt.AddMinutes(1), costBreach: false);
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

    private static async Task SeedDigestAsync(
        TestDatabase database, Guid runId, List<(Guid jobId, int rank)> cards)
    {
        var digestId = Guid.CreateVersion7();
        var digestCards = cards
            .Select(c => new DigestCard(
                Guid.CreateVersion7(), digestId, c.jobId, runId, c.rank, score: 80m, ["A reason"], applyUrlVerified: true))
            .ToList();

        var digest = new Digest(
            digestId, runId, DigestMode.Full, totalNewJobs: cards.Count, strongMatches: cards.Count,
            avgSalaryUsd: null, suppressedCount: 0, [], carriedOverCount: 0, companiesChecked: 1,
            analysedCount: cards.Count, [], narrative: null, NarrativeSource.Template, promptVersion: null,
            digestCards, RunStart);

        await using var ctx = database.CreateContext();
        ctx.Add(digest);
        await ctx.SaveChangesAsync();
    }

    private static async Task SeedDeliveryAsync(
        TestDatabase database, Guid runId, Guid jobId, DateTimeOffset deliveredAt)
    {
        var record = new DeliveryRecord(
            Guid.CreateVersion7(), runId, chatId: OwnerChat, CardKey.For(runId, jobId), telegramMessageId: 100, deliveredAt);

        var log = new DeliveryLog(new NpgsqlConnectionFactory(database.ConnectionString));
        (await log.TryRecordAsync(record)).ShouldBeTrue();
    }

    private static async Task SeedRatedAsync(TestDatabase database, Guid jobId)
    {
        var facts = JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
        {
            [Dimension.Country] = ["DE"],
        });
        var signal = Signal.Capture(
            Guid.CreateVersion7(), jobId, applicationId: null, SignalKind.Rated, facts, DeliveredAt, SignalWeights.Default);

        var repo = new SignalRepository(new NpgsqlConnectionFactory(database.ConnectionString));
        (await repo.TryCaptureAsync(signal)).ShouldBeTrue();
    }
}
