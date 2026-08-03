using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// T09 / O3: the 90-day retention prune deletes raw postings gone cold — last seen before the cutoff — but
/// never one still referenced by a job's <c>job_aliases</c> row, which is the provenance a live or closed
/// job depends on. The <c>NOT EXISTS</c> makes that intent explicit and the restrict FK is the backstop;
/// this suite proves both: a cold, unreferenced posting is removed, a cold but referenced one is kept, and
/// a fresh one is untouched. Requires Docker.
/// </summary>
public sealed class RawPostingRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Cutoff = Now.AddDays(-90);
    private static readonly DateTimeOffset Cold = Cutoff.AddDays(-1);
    private static readonly DateTimeOffset Fresh = Cutoff.AddDays(+1);

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

    private static async Task<Guid> InsertPostingAsync(Seed seed, string externalId, DateTimeOffset lastSeen)
    {
        var id = Guid.CreateVersion7();
        var payload = $"{{\"external_id\":\"{externalId}\"}}";
        await using var ctx = seed.Database.CreateContext();
        var posting = new RawPosting(id, seed.SourceId, externalId, ContentHash.Compute(payload), payload, 200, Cold.AddDays(-10));
        ctx.Add(posting);
        await ctx.SaveChangesAsync();

        // Set last_seen_at explicitly — the domain sets it to fetched_at at construction.
        await using var connection = new Npgsql.NpgsqlConnection(seed.Database.ConnectionString);
        await connection.OpenAsync();
        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE raw_postings SET last_seen_at = @ls WHERE id = @id";
        update.Parameters.AddWithValue("ls", lastSeen);
        update.Parameters.AddWithValue("id", id);
        await update.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task<long> CountPostingsAsync(Seed seed)
    {
        await using var read = seed.Database.CreateContext();
        return await read.Set<RawPosting>().LongCountAsync();
    }

    private static RawPostingRepository NewRepository(Seed seed) =>
        new(new NpgsqlConnectionFactory(seed.Database.ConnectionString));

    [RequiresDockerFact]
    public async Task A_cold_unreferenced_posting_is_pruned()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        await InsertPostingAsync(seed, "cold-1", Cold);

        var pruned = await NewRepository(seed).PruneOlderThanAsync(Cutoff);

        pruned.ShouldBe(1);
        (await CountPostingsAsync(seed)).ShouldBe(0);
    }

    [RequiresDockerFact]
    public async Task A_fresh_posting_is_never_pruned()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        await InsertPostingAsync(seed, "fresh-1", Fresh);

        var pruned = await NewRepository(seed).PruneOlderThanAsync(Cutoff);

        pruned.ShouldBe(0);
        (await CountPostingsAsync(seed)).ShouldBe(1);
    }

    [RequiresDockerFact]
    public async Task A_cold_posting_still_referenced_by_a_live_alias_is_never_pruned()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        // A cold posting that a live job still points to through job_aliases: it is the opening's provenance.
        var origin = await InsertPostingAsync(seed, "referenced-1", Cold);

        var job = new Job(
            Guid.CreateVersion7(),
            seed.CompanyId,
            origin,
            Fingerprint.TryCreate(new string('a', 64)).Value,
            fingerprintVersion: 1,
            title: "Staff SRE",
            normalisedTitle: "staff sre",
            description: "We run reliable systems.",
            applyUrl: "https://acme.com/apply/1",
            LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            RemotePolicy.Hybrid,
            EmploymentType.FullTime,
            PostedAtGranularity.Day,
            firstSeenAt: Cold,
            lastSeenAt: Cold);
        job.RegisterAlias(origin, seed.SourceId, Cold, Cold);

        var jobs = new JobRepository(seed.Database.CreateContext(), new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        await jobs.InsertAsync(job);

        var pruned = await NewRepository(seed).PruneOlderThanAsync(Cutoff);

        // The referenced posting survives even though it is cold: the NOT EXISTS excludes it, and the
        // restrict FK would have refused the delete regardless.
        pruned.ShouldBe(0);
        (await CountPostingsAsync(seed)).ShouldBe(1);
    }

    [RequiresDockerFact]
    public async Task Pruning_is_idempotent()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        await InsertPostingAsync(seed, "cold-1", Cold);

        (await NewRepository(seed).PruneOlderThanAsync(Cutoff)).ShouldBe(1);
        (await NewRepository(seed).PruneOlderThanAsync(Cutoff)).ShouldBe(0);
    }

    private sealed record Seed(TestDatabase Database, Guid CompanyId, Guid SourceId);
}
