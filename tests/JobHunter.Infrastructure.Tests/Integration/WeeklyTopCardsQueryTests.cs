using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Reporting;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Shouldly;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F4 T20 done-when 1: the weekly rating loop asks about the previous week's top-ten <em>delivered</em> cards.
/// The read model returns exactly those — a card assembled but never sent is excluded, a delivery outside the
/// half-open window is excluded, the order is by rank, and the denominator is capped at ten. It selects nothing
/// about the Owner's CV. Requires Docker.
/// </summary>
public sealed class WeeklyTopCardsQueryTests
{
    private static readonly DateTimeOffset FirstSeen = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RunStart = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    // The window under review: the week [WeekFrom, WeekTo).
    private static readonly DateTimeOffset WeekFrom = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WeekTo = new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task Returns_the_weeks_delivered_cards_ordered_by_rank()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);

        var top = await SeedJobAsync(database, companyId);
        var second = await SeedJobAsync(database, companyId);
        await SeedDigestAsync(database, runId, [(second, 2), (top, 1)]);
        await SeedDeliveryAsync(database, runId, top, WeekFrom.AddHours(7));
        await SeedDeliveryAsync(database, runId, second, WeekFrom.AddHours(7));

        var query = new WeeklyTopCardsQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var cards = await query.TopCardsAsync(WeekFrom, WeekTo);

        cards.Select(c => c.JobId).ShouldBe([top, second]);
        cards.Select(c => c.Rank).ShouldBe([1, 2]);
        cards[0].RunId.ShouldBe(runId);
    }

    [RequiresDockerFact]
    public async Task An_assembled_but_undelivered_card_is_excluded()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);

        var delivered = await SeedJobAsync(database, companyId);
        var undelivered = await SeedJobAsync(database, companyId);
        await SeedDigestAsync(database, runId, [(delivered, 1), (undelivered, 2)]);
        await SeedDeliveryAsync(database, runId, delivered, WeekFrom.AddHours(7));

        var query = new WeeklyTopCardsQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var cards = await query.TopCardsAsync(WeekFrom, WeekTo);

        cards.Select(c => c.JobId).ShouldBe([delivered]);
    }

    [RequiresDockerFact]
    public async Task A_delivery_outside_the_window_is_excluded()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);

        var inWindow = await SeedJobAsync(database, companyId);
        var beforeWindow = await SeedJobAsync(database, companyId);
        await SeedDigestAsync(database, runId, [(inWindow, 1), (beforeWindow, 2)]);
        await SeedDeliveryAsync(database, runId, inWindow, WeekFrom.AddHours(7));
        await SeedDeliveryAsync(database, runId, beforeWindow, WeekFrom.AddSeconds(-1));

        var query = new WeeklyTopCardsQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var cards = await query.TopCardsAsync(WeekFrom, WeekTo);

        cards.Select(c => c.JobId).ShouldBe([inWindow]);
    }

    [RequiresDockerFact]
    public async Task At_most_ten_cards_are_returned()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);

        var ranked = new List<(Guid jobId, int rank)>();
        for (var rank = 1; rank <= 12; rank++)
        {
            ranked.Add((await SeedJobAsync(database, companyId), rank));
        }

        await SeedDigestAsync(database, runId, ranked);
        foreach (var (jobId, _) in ranked)
        {
            await SeedDeliveryAsync(database, runId, jobId, WeekFrom.AddHours(7));
        }

        var query = new WeeklyTopCardsQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var cards = await query.TopCardsAsync(WeekFrom, WeekTo);

        cards.Count.ShouldBe(10);
        cards.Select(c => c.Rank).ShouldBe(Enumerable.Range(1, 10));
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

    private static async Task<Guid> SeedDigestAsync(
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
        return digestId;
    }

    private static async Task SeedDeliveryAsync(
        TestDatabase database, Guid runId, Guid jobId, DateTimeOffset deliveredAt)
    {
        var record = new DeliveryRecord(
            Guid.CreateVersion7(), runId, chatId: 4242, CardKey.For(runId, jobId), telegramMessageId: 100, deliveredAt);

        var log = new DeliveryLog(new NpgsqlConnectionFactory(database.ConnectionString));
        (await log.TryRecordAsync(record)).ShouldBeTrue();
    }
}
