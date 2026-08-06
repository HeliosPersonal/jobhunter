using JobHunter.Domain.Preferences;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F7 T07 (data-model §suppression_overrides): the read side of the Owner's stated override rules. Ranking
/// loads them once per Run and matches them against each job's facts. The query round-trips the enum-as-text
/// <c>dimension</c> and <c>mode</c> columns back into the domain <see cref="SuppressionOverride"/>, returns an
/// empty list when none is set (the common day), and never writes (architecture rule 4). Requires Docker.
/// </summary>
public sealed class SuppressionOverrideQueryTests
{
    private static readonly DateTimeOffset When = new(2026, 8, 7, 7, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task It_returns_nothing_when_no_override_is_set()
    {
        await using var database = await TestDatabase.CreateAsync();

        var rules = await NewQuery(database).AllAsync();

        rules.ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task It_loads_each_override_with_its_dimension_value_and_mode()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedAsync(database,
            new SuppressionOverride(Guid.CreateVersion7(), Dimension.Country, "DE", SuppressionMode.NeverSuppress, When),
            new SuppressionOverride(Guid.CreateVersion7(), Dimension.RoleFamily, "MlResearch", SuppressionMode.AlwaysSuppress, When));

        var rules = await NewQuery(database).AllAsync();

        rules.Count.ShouldBe(2);
        var country = rules.Single(r => r.Dimension == Dimension.Country);
        country.Value.ShouldBe("DE");
        country.Mode.ShouldBe(SuppressionMode.NeverSuppress);
        rules.Single(r => r.Dimension == Dimension.RoleFamily).Mode.ShouldBe(SuppressionMode.AlwaysSuppress);
    }

    private static SuppressionOverrideQuery NewQuery(TestDatabase database) =>
        new(new NpgsqlConnectionFactory(database.ConnectionString));

    private static async Task SeedAsync(TestDatabase database, params SuppressionOverride[] overrides)
    {
        await using var ctx = database.CreateContext();
        foreach (var o in overrides)
        {
            ctx.Add(o);
        }

        await ctx.SaveChangesAsync();
    }
}
