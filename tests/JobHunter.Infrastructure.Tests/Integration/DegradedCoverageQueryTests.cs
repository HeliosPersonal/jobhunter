using JobHunter.Domain.Companies;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// T12 / AC-09: the degraded-coverage query returns the sources whose quarantine window has not yet
/// expired at the read instant — the companies whose boards are not being fetched, joined to their name
/// and provider for the digest footer — and excludes healthy sources and expired quarantines. Requires
/// Docker.
/// </summary>
public sealed class DegradedCoverageQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 7, 0, 0, TimeSpan.Zero);

    private static async Task<(TestDatabase Db, Guid QuarantinedSourceId)> SeededAsync()
    {
        var database = await TestDatabase.CreateAsync();
        var clock = new FakeClock(Now);

        await using var ctx = database.CreateContext();

        // A quarantined source (two failures, quarantined for 24h) — degraded.
        var (badCompany, badBinding, badSource) = Seed(ctx, "acme.com", "Acme", "acme");
        badSource.RecordFailure(clock, TimeSpan.FromHours(24));
        badSource.RecordFailure(clock, TimeSpan.FromHours(24));

        // A healthy source — not degraded.
        Seed(ctx, "globex.com", "Globex", "globex");

        // A source whose quarantine has already lapsed by Now — not degraded any more.
        var (_, _, expiredSource) = Seed(ctx, "initech.com", "Initech", "initech");
        var past = new FakeClock(Now.AddHours(-25));
        expiredSource.RecordFailure(past, TimeSpan.FromHours(24));
        expiredSource.RecordFailure(past, TimeSpan.FromHours(24));

        await ctx.SaveChangesAsync();
        return (database, badSource.Id);
    }

    private static (Company, AtsBinding, JobSource) Seed(JobHunterDbContext ctx, string domain, string name, string token)
    {
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();

        var company = new Company(companyId, CanonicalDomain.TryCreate(domain).Value, name, CompanySource.Curated, Now);
        var binding = new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, token, BindingConfidence.TryCreate(0.9m).Value, "{}", Now);
        var source = new JobSource(sourceId, companyId, bindingId, $"https://boards-api.greenhouse.io/v1/boards/{token}/jobs");

        ctx.Add(company);
        ctx.Add(binding);
        ctx.Add(source);
        return (company, binding, source);
    }

    [RequiresDockerFact]
    public async Task Returns_only_sources_still_inside_their_quarantine_window()
    {
        var (database, quarantinedSourceId) = await SeededAsync();
        await using var _ = database;
        var query = new DegradedCoverageQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        var degraded = await query.DegradedSourcesAsync(Now);

        var row = degraded.ShouldHaveSingleItem();
        row.SourceId.ShouldBe(quarantinedSourceId);
        row.CompanyName.ShouldBe("Acme");
        row.AtsKind.ShouldBe(nameof(AtsKind.Greenhouse));
        row.ConsecutiveFailures.ShouldBe(2);
        row.QuarantinedUntil.ToUniversalTime().ShouldBe(Now.AddHours(24));
    }
}
