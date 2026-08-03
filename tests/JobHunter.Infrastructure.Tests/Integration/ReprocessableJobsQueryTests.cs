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
/// T09 / AC-09: the reprocessable-jobs read streams live and closed jobs first seen at or after the cutoff,
/// oldest first, each with the origin raw posting the service re-reads to re-normalise. Quarantined and
/// superseded jobs are excluded — reprocessing never disturbs a terminal state — and the char(64)
/// fingerprint is returned untrimmed of the stored value so the service compares it Ordinal. Requires Docker.
/// </summary>
public sealed class ReprocessableJobsQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    private static async Task<Seed> SeedAsync()
    {
        var database = await TestDatabase.CreateAsync();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now));
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", Now));
        ctx.Add(new JobSource(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs"));
        await ctx.SaveChangesAsync();

        return new Seed(database, companyId, sourceId);
    }

    private static string Hex(char c) => new(c, 64);

    private static async Task<Job> InsertJobAsync(
        Seed seed,
        char fingerprint,
        DateTimeOffset firstSeen,
        Action<Job>? mutate = null)
    {
        var origin = Guid.CreateVersion7();
        var payload = $"{{\"fp\":\"{fingerprint}\"}}";

        await using (var ctx = seed.Database.CreateContext())
        {
            ctx.Add(new RawPosting(origin, seed.SourceId, $"job-{fingerprint}", ContentHash.Compute(payload), payload, 200, firstSeen));
            await ctx.SaveChangesAsync();
        }

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
            LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            RemotePolicy.Hybrid,
            EmploymentType.FullTime,
            PostedAtGranularity.Day,
            firstSeenAt: firstSeen,
            lastSeenAt: firstSeen);
        job.RegisterAlias(origin, seed.SourceId, firstSeen, firstSeen);

        var repo = new JobRepository(seed.Database.CreateContext(), new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        await repo.InsertAsync(job);

        if (mutate is not null)
        {
            var tracked = await repo.FindAsync(job.Id);
            mutate(tracked!);
            await repo.SaveChangesAsync();
        }

        return job;
    }

    private static ReprocessableJobsQuery NewQuery(Seed seed) =>
        new(new NpgsqlConnectionFactory(seed.Database.ConnectionString));

    private static async Task<List<ReprocessableJob>> DrainAsync(Seed seed, DateTimeOffset from)
    {
        var results = new List<ReprocessableJob>();
        await foreach (var row in NewQuery(seed).StreamAsync(from))
        {
            results.Add(row);
        }

        return results;
    }

    [RequiresDockerFact]
    public async Task It_streams_live_and_closed_jobs_oldest_first_with_their_origin_and_trimmed_fingerprint()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var older = await InsertJobAsync(seed, 'a', Now.AddDays(-3));
        var newer = await InsertJobAsync(seed, 'b', Now.AddDays(-1), j => j.Close(Now));

        var rows = await DrainAsync(seed, Now.AddDays(-5));

        rows.Count.ShouldBe(2);
        rows[0].JobId.ShouldBe(older.Id);          // oldest first
        rows[1].JobId.ShouldBe(newer.Id);
        rows[0].Fingerprint.ShouldBe(Hex('a'));    // char(64) padding trimmed for an Ordinal compare
        rows[0].OriginRawPostingId.ShouldBe(older.OriginRawPostingId);
        rows[0].CompanyId.ShouldBe(seed.CompanyId);
    }

    [RequiresDockerFact]
    public async Task It_excludes_quarantined_and_superseded_jobs()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var live = await InsertJobAsync(seed, 'a', Now.AddDays(-2));
        await InsertJobAsync(seed, 'b', Now.AddDays(-2), j => j.Quarantine());
        await InsertJobAsync(seed, 'c', Now.AddDays(-2), j => j.Supersede(Guid.CreateVersion7(), Now));

        var rows = await DrainAsync(seed, Now.AddDays(-5));

        rows.ShouldHaveSingleItem().JobId.ShouldBe(live.Id);
    }

    [RequiresDockerFact]
    public async Task It_excludes_jobs_first_seen_before_the_cutoff()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        await InsertJobAsync(seed, 'a', Now.AddDays(-10));
        var recent = await InsertJobAsync(seed, 'b', Now.AddDays(-1));

        var rows = await DrainAsync(seed, Now.AddDays(-5));

        rows.ShouldHaveSingleItem().JobId.ShouldBe(recent.Id);
    }

    private sealed record Seed(TestDatabase Database, Guid CompanyId, Guid SourceId);
}
