using JobHunter.Domain.Companies;
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
/// T13 / SAD §6.1: the closure sweep query returns the raw postings whose <c>last_seen_at</c> is strictly
/// before the cutoff — gone from their board — and excludes postings re-seen this cycle. It leans on the
/// T11 upsert bumping <c>last_seen_at</c> on an unchanged re-fetch, so a reappearing posting is not a
/// closure candidate. Requires Docker.
/// </summary>
public sealed class ClosureSweepQueryTests
{
    private static readonly DateTimeOffset FirstCycle = new(2026, 8, 1, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondCycle = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(TestDatabase Db, Guid SourceId)> SeededSourceAsync()
    {
        var database = await TestDatabase.CreateAsync();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, FirstCycle));
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", FirstCycle));
        ctx.Add(new JobSource(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs"));
        await ctx.SaveChangesAsync();

        return (database, sourceId);
    }

    private static RawPosting Posting(Guid sourceId, string externalId, string payload, DateTimeOffset at) =>
        new(Guid.CreateVersion7(), sourceId, externalId, ContentHash.Compute(payload), payload, 200, at);

    [RequiresDockerFact]
    public async Task Returns_only_postings_whose_last_seen_did_not_advance_this_cycle()
    {
        var (database, sourceId) = await SeededSourceAsync();
        await using var _ = database;
        var factory = new NpgsqlConnectionFactory(database.ConnectionString);
        var repo = new RawPostingRepository(factory);

        // Both postings ingested in the first cycle.
        await repo.IngestAsync(Posting(sourceId, "job-live", "{\"t\":\"live\"}", FirstCycle));
        await repo.IngestAsync(Posting(sourceId, "job-gone", "{\"t\":\"gone\"}", FirstCycle));

        // Second cycle re-sees only job-live (unchanged upsert bumps last_seen_at); job-gone is absent.
        await repo.IngestAsync(Posting(sourceId, "job-live", "{\"t\":\"live\"}", SecondCycle));

        var query = new ClosureSweepQuery(factory);
        // Cutoff = the second cycle instant: a posting last seen before it was not re-seen this cycle.
        var closed = await query.ClosedSinceAsync(SecondCycle);

        var row = closed.ShouldHaveSingleItem();
        row.LastSeenAt.ToUniversalTime().ShouldBe(FirstCycle);
    }

    [RequiresDockerFact]
    public async Task A_posting_that_reappeared_before_the_sweep_is_not_a_candidate()
    {
        var (database, sourceId) = await SeededSourceAsync();
        await using var _ = database;
        var factory = new NpgsqlConnectionFactory(database.ConnectionString);
        var repo = new RawPostingRepository(factory);

        await repo.IngestAsync(Posting(sourceId, "job-1", "{\"t\":\"v1\"}", FirstCycle));
        await repo.IngestAsync(Posting(sourceId, "job-1", "{\"t\":\"v1\"}", SecondCycle));

        var query = new ClosureSweepQuery(factory);
        var closed = await query.ClosedSinceAsync(SecondCycle);

        closed.ShouldBeEmpty();
    }
}
