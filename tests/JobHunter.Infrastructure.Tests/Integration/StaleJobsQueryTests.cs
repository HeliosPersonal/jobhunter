using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// T08 / SAD §6.2: the job-liveness read returns the live jobs whose <em>every</em> alias has gone stale —
/// the latest <c>last_seen_at</c> across the job is strictly before the cutoff — with that same latest
/// sighting as the closure instant (AC-06). A job with even one fresh alias is excluded, and closure is
/// suspended for a job any of whose contributing sources is still quarantined (§D4). Requires Docker.
/// </summary>
public sealed class StaleJobsQueryTests
{
    private static readonly DateTimeOffset FirstCycle = new(2026, 8, 1, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Cutoff = new(2026, 8, 3, 1, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Stale = Cutoff.AddHours(-6);
    private static readonly DateTimeOffset Fresh = Cutoff.AddHours(+1);

    private static async Task<Seed> SeedAsync()
    {
        var database = await TestDatabase.CreateAsync();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var healthySourceId = Guid.CreateVersion7();
        var quarantinedSourceId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, FirstCycle));
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", FirstCycle));

        var healthy = new JobSource(healthySourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs");
        var quarantined = new JobSource(quarantinedSourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme2/jobs");
        // Quarantine the second source until well after the cutoff.
        quarantined.RecordFailure(new FakeClock(Cutoff.AddHours(-2)), TimeSpan.FromHours(24));
        quarantined.RecordFailure(new FakeClock(Cutoff.AddHours(-2)), TimeSpan.FromHours(24));
        ctx.Add(healthy);
        ctx.Add(quarantined);
        await ctx.SaveChangesAsync();

        return new Seed(database, companyId, healthySourceId, quarantinedSourceId);
    }

    private static string Hex(char c) => new(c, 64);

    private static async Task<Job> InsertJobAsync(
        Seed seed,
        char fingerprint,
        params (Guid SourceId, DateTimeOffset LastSeen)[] aliases)
    {
        var locations = LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]);
        var origin = Guid.CreateVersion7();

        var job = new Job(
            Guid.CreateVersion7(),
            seed.CompanyId,
            origin,
            Fingerprint.TryCreate(Hex(fingerprint)).Value,
            fingerprintVersion: 1,
            title: "Staff SRE",
            normalisedTitle: "staff sre",
            description: "We run reliable systems.",
            applyUrl: "https://acme.com/apply/1",
            locations,
            RemotePolicy.Hybrid,
            EmploymentType.FullTime,
            PostedAtGranularity.Day,
            firstSeenAt: FirstCycle,
            lastSeenAt: FirstCycle);

        // Seed one raw posting per alias, then register the aliases on the aggregate.
        var rawIds = new List<Guid>();
        await using (var ctx = seed.Database.CreateContext())
        {
            for (var i = 0; i < aliases.Length; i++)
            {
                var rawId = i == 0 ? origin : Guid.CreateVersion7();
                rawIds.Add(rawId);
                var externalId = $"{fingerprint}-{i}";
                ctx.Add(new RawPosting(rawId, aliases[i].SourceId, externalId, ContentHash.Compute(externalId), "{}", 200, FirstCycle));
            }

            await ctx.SaveChangesAsync();
        }

        for (var i = 0; i < aliases.Length; i++)
        {
            job.RegisterAlias(rawIds[i], aliases[i].SourceId, FirstCycle, aliases[i].LastSeen);
        }

        var repo = new JobRepository(seed.Database.CreateContext(), new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        await repo.InsertAsync(job);
        return job;
    }

    private static StaleJobsQuery NewQuery(Seed seed) =>
        new(new NpgsqlConnectionFactory(seed.Database.ConnectionString));

    [RequiresDockerFact]
    public async Task Returns_a_job_whose_every_alias_is_stale_with_the_latest_sighting_as_the_closure_instant()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var older = Stale.AddHours(-3);
        var job = await InsertJobAsync(
            seed,
            'a',
            (seed.HealthySourceId, older),
            (seed.HealthySourceId, Stale));

        var stale = await NewQuery(seed).StaleSinceAsync(Cutoff, Cutoff);

        var row = stale.ShouldHaveSingleItem();
        row.JobId.ShouldBe(job.Id);
        // The closure instant is the job's own latest alias sighting, not the oldest.
        row.LastSeenAt.ToUniversalTime().ShouldBe(Stale);
    }

    [RequiresDockerFact]
    public async Task A_job_with_one_fresh_alias_is_not_stale()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        await InsertJobAsync(
            seed,
            'b',
            (seed.HealthySourceId, Stale),
            (seed.HealthySourceId, Fresh));

        var stale = await NewQuery(seed).StaleSinceAsync(Cutoff, Cutoff);

        stale.ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task A_stale_job_on_a_quarantined_source_is_suspended_from_closure()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        // Every alias is stale, but one sits on a source still inside its quarantine window at the cutoff.
        await InsertJobAsync(
            seed,
            'c',
            (seed.HealthySourceId, Stale),
            (seed.QuarantinedSourceId, Stale.AddHours(-1)));

        var stale = await NewQuery(seed).StaleSinceAsync(Cutoff, Cutoff);

        stale.ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task Quarantine_that_has_already_expired_no_longer_suspends_closure()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        await InsertJobAsync(
            seed,
            'd',
            (seed.QuarantinedSourceId, Stale));

        // As-of an instant after the 24h quarantine window has elapsed, the source no longer protects the job.
        var asOf = Cutoff.AddHours(48);
        var stale = await NewQuery(seed).StaleSinceAsync(Cutoff, asOf);

        stale.ShouldHaveSingleItem();
    }

    private sealed record Seed(TestDatabase Database, Guid CompanyId, Guid HealthySourceId, Guid QuarantinedSourceId);
}
