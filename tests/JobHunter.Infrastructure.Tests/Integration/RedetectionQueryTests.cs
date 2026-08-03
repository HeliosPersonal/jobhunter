using JobHunter.Domain.Companies;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// T09 / AC-05: the re-detection read model returns the companies to re-probe this run — those whose live
/// binding is stale (detected before the cutoff) or whose board's last two successful fetches all returned
/// zero postings — and no others. It also spreads the week: a company falls into exactly one day bucket by
/// a stable hash of its id, so a run reads only that day's bucket. A board with a single empty cycle, or a
/// recent non-empty fetch, is not a candidate — a board that legitimately has openings is never re-probed
/// on the empty-cycle basis. Requires Docker.
/// </summary>
public sealed class RedetectionQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 3, 30, 0, TimeSpan.Zero);

    private static (Company, AtsBinding, JobSource) Seed(
        JobHunterDbContext ctx, string domain, string name, string token, DateTimeOffset detectedAt)
    {
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();

        var company = new Company(companyId, CanonicalDomain.TryCreate(domain).Value, name, CompanySource.Curated, Now);
        var binding = new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, token, BindingConfidence.TryCreate(0.9m).Value, "{}", detectedAt);
        var source = new JobSource(sourceId, companyId, bindingId, $"https://boards-api.greenhouse.io/v1/boards/{token}/jobs");

        ctx.Add(company);
        ctx.Add(binding);
        ctx.Add(source);
        return (company, binding, source);
    }

    private static SourceFetchLog SuccessLog(Guid sourceId, int postingsReturned, DateTimeOffset startedAt) =>
        new(Guid.CreateVersion7(), sourceId, startedAt, 10, 200, postingsReturned, 0, FetchOutcome.Success);

    [RequiresDockerFact]
    public async Task Returns_a_company_whose_live_binding_is_stale()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;

        Guid staleCompanyId;
        await using (var ctx = database.CreateContext())
        {
            var (stale, _, _) = Seed(ctx, "acme.com", "Acme", "acme", Now.AddDays(-30));
            Seed(ctx, "globex.com", "Globex", "globex", Now.AddDays(-1)); // fresh binding, no empty cycles
            staleCompanyId = stale.Id;
            await ctx.SaveChangesAsync();
        }

        var query = new RedetectionQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        // bucketCount 1 collapses the spread so every candidate is in bucket 0 — isolates the staleness rule.
        var due = await query.DueCandidatesAsync(Now.AddDays(-7), emptyCycles: 2, dayBucket: 0, bucketCount: 1);

        var row = due.ShouldHaveSingleItem();
        row.CompanyId.ShouldBe(staleCompanyId);
    }

    [RequiresDockerFact]
    public async Task Returns_a_fresh_company_only_after_two_consecutive_empty_cycles()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;

        Guid emptyBoardCompanyId;
        await using (var ctx = database.CreateContext())
        {
            // Fresh binding, but its last two successful fetches returned zero postings — a candidate.
            var (empty, _, emptySource) = Seed(ctx, "acme.com", "Acme", "acme", Now.AddDays(-1));
            ctx.Add(SuccessLog(emptySource.Id, 0, Now.AddHours(-12)));
            ctx.Add(SuccessLog(emptySource.Id, 0, Now.AddHours(-6)));
            emptyBoardCompanyId = empty.Id;

            // Fresh binding, only one empty success so far — not yet a candidate.
            var (_, _, onceSource) = Seed(ctx, "globex.com", "Globex", "globex", Now.AddDays(-1));
            ctx.Add(SuccessLog(onceSource.Id, 0, Now.AddHours(-6)));

            // Fresh binding, most recent fetch returned postings — a live board, never a candidate.
            var (_, _, liveSource) = Seed(ctx, "initech.com", "Initech", "initech", Now.AddDays(-1));
            ctx.Add(SuccessLog(liveSource.Id, 0, Now.AddHours(-12)));
            ctx.Add(SuccessLog(liveSource.Id, 5, Now.AddHours(-6)));

            await ctx.SaveChangesAsync();
        }

        var query = new RedetectionQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var due = await query.DueCandidatesAsync(Now.AddDays(-7), emptyCycles: 2, dayBucket: 0, bucketCount: 1);

        var row = due.ShouldHaveSingleItem();
        row.CompanyId.ShouldBe(emptyBoardCompanyId);
    }

    [RequiresDockerFact]
    public async Task A_candidate_falls_into_exactly_one_day_bucket_so_the_week_is_spread()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;

        await using (var ctx = database.CreateContext())
        {
            Seed(ctx, "acme.com", "Acme", "acme", Now.AddDays(-30)); // stale, so always a candidate
            await ctx.SaveChangesAsync();
        }

        var query = new RedetectionQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        var hits = 0;
        for (var bucket = 0; bucket < 7; bucket++)
        {
            var due = await query.DueCandidatesAsync(Now.AddDays(-7), emptyCycles: 2, dayBucket: bucket, bucketCount: 7);
            hits += due.Count;
        }

        // The one stale company appears on exactly one of the seven days — never zero, never every day.
        hits.ShouldBe(1);
    }
}
