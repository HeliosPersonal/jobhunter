using JobHunter.Domain.Companies;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// The EF Core write repository for operational sources and their fetch logs (data-model §job_sources,
/// §source_fetch_log). It proves the round-trip each discovery handler depends on: a source is added and
/// found by id and by binding, a fetch log is appended, and a lookup that matches nothing returns null
/// rather than throwing. Requires Docker.
/// </summary>
public sealed class JobSourceRepositoryTests
{
    private static readonly DateTimeOffset SeenAt = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task An_added_source_is_found_by_id_and_by_binding()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var (companyId, bindingId) = await SeedCompanyAndBindingAsync(database);
        var sourceId = Guid.CreateVersion7();

        await using (var ctx = database.CreateContext())
        {
            var repo = new JobSourceRepository(ctx);
            await repo.AddAsync(new JobSource(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs"));
            (await repo.SaveChangesAsync()).ShouldBe(1);
        }

        await using (var ctx = database.CreateContext())
        {
            var repo = new JobSourceRepository(ctx);
            (await repo.FindAsync(sourceId)).ShouldNotBeNull().Id.ShouldBe(sourceId);
            (await repo.FindByBindingAsync(bindingId)).ShouldNotBeNull().BindingId.ShouldBe(bindingId);
        }
    }

    [RequiresDockerFact]
    public async Task A_lookup_that_matches_nothing_returns_null()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        await using var ctx = database.CreateContext();
        var repo = new JobSourceRepository(ctx);

        (await repo.FindAsync(Guid.CreateVersion7())).ShouldBeNull();
        (await repo.FindByBindingAsync(Guid.CreateVersion7())).ShouldBeNull();
    }

    [RequiresDockerFact]
    public async Task A_fetch_log_is_appended_for_a_source()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var (companyId, bindingId) = await SeedCompanyAndBindingAsync(database);
        var sourceId = Guid.CreateVersion7();

        await using (var ctx = database.CreateContext())
        {
            var repo = new JobSourceRepository(ctx);
            await repo.AddAsync(new JobSource(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs"));
            await repo.AddFetchLogAsync(new SourceFetchLog(
                Guid.CreateVersion7(), sourceId, SeenAt, durationMs: 120, httpStatus: 200,
                postingsReturned: 5, postingsChanged: 2, FetchOutcome.Success));
            (await repo.SaveChangesAsync()).ShouldBe(2);
        }

        await using var verify = database.CreateContext();
        (await new JobSourceRepository(verify).FindAsync(sourceId)).ShouldNotBeNull();
    }

    private static async Task<(Guid CompanyId, Guid BindingId)> SeedCompanyAndBindingAsync(TestDatabase database)
    {
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, SeenAt));
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", SeenAt));
        await ctx.SaveChangesAsync();
        return (companyId, bindingId);
    }
}
