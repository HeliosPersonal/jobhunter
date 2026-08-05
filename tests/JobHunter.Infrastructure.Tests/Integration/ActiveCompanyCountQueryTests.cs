using JobHunter.Domain.Companies;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F5 T09 / AC-05: the active-company count is the scope stated on a <c>NothingNew</c> day ("we scanned N
/// companies and nothing new came up"). It counts only companies flagged <c>is_active</c> — a retired
/// company is out of scope — and runs the same SQL against the same partial index the digest header reads.
/// Requires Docker.
/// </summary>
public sealed class ActiveCompanyCountQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 7, 0, 0, TimeSpan.Zero);

    private static Company Company(string domain, string name, bool isActive) =>
        new(Guid.CreateVersion7(), CanonicalDomain.TryCreate(domain).Value, name, CompanySource.Curated, Now,
            isActive: isActive);

    [RequiresDockerFact]
    public async Task Counts_only_active_companies()
    {
        await using var database = await TestDatabase.CreateAsync();

        await using (var ctx = database.CreateContext())
        {
            ctx.Add(Company("acme.com", "Acme", isActive: true));
            ctx.Add(Company("globex.com", "Globex", isActive: true));
            ctx.Add(Company("initech.com", "Initech", isActive: false));
            await ctx.SaveChangesAsync();
        }

        var query = new ActiveCompanyCountQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        (await query.ActiveCompanyCountAsync()).ShouldBe(2);
    }

    [RequiresDockerFact]
    public async Task An_empty_registry_is_a_scope_of_zero()
    {
        await using var database = await TestDatabase.CreateAsync();
        var query = new ActiveCompanyCountQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        (await query.ActiveCompanyCountAsync()).ShouldBe(0);
    }
}
