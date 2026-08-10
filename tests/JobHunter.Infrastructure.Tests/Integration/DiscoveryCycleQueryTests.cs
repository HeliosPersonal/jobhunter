using JobHunter.Domain.Companies;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// The read side of the discovery cycle (SAD §6.1, AC-01): the sources due for a fetch this window. A source
/// is due only when its company is active, its binding is live and confident (≥ 0.80), it is not currently
/// quarantined, and it was not fetched since the recent-refetch cutoff. This suite drives each exclusion
/// predicate independently — an inactive company, a retired binding, a sub-threshold binding, an unexpired
/// quarantine and a too-recent fetch each drop a source — and confirms the happy arms: a never-fetched
/// source, a source whose quarantine has expired, and a source last fetched before the cutoff are all due,
/// and the curated comp band is carried through. Requires Docker.
/// </summary>
public sealed class DiscoveryCycleQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FetchedBefore = new(2026, 8, 2, 5, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task A_confident_live_active_never_fetched_source_is_due_and_carries_its_comp_band()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var sourceId = await SeedSourceAsync(database, compBand: "High", remoteEmeaFriendly: true);

        var due = await Query(database).DueSourcesAsync(Now, FetchedBefore);

        var row = due.ShouldHaveSingleItem();
        row.SourceId.ShouldBe(sourceId);
        row.CompBand.ShouldBe("High");
        row.RemoteEmeaFriendly.ShouldBe(true);
    }

    [RequiresDockerFact]
    public async Task An_inactive_company_is_not_due()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        await SeedSourceAsync(database, isActive: false);

        (await Query(database).DueSourcesAsync(Now, FetchedBefore)).ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task A_retired_binding_is_not_due()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        await SeedSourceAsync(database, retireBinding: true);

        (await Query(database).DueSourcesAsync(Now, FetchedBefore)).ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task A_sub_threshold_binding_is_not_due()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        await SeedSourceAsync(database, confidence: 0.79m);

        (await Query(database).DueSourcesAsync(Now, FetchedBefore)).ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task A_source_inside_its_quarantine_window_is_not_due()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var sourceId = await SeedSourceAsync(database);
        await ExecuteAsync(database,
            "UPDATE job_sources SET quarantined_until = @until WHERE id = @id",
            sourceId, ("until", Now.AddHours(1)));

        (await Query(database).DueSourcesAsync(Now, FetchedBefore)).ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task A_source_whose_quarantine_has_expired_is_due_again()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var sourceId = await SeedSourceAsync(database);
        await ExecuteAsync(database,
            "UPDATE job_sources SET quarantined_until = @until WHERE id = @id",
            sourceId, ("until", Now.AddHours(-1)));

        (await Query(database).DueSourcesAsync(Now, FetchedBefore)).ShouldHaveSingleItem().SourceId.ShouldBe(sourceId);
    }

    [RequiresDockerFact]
    public async Task A_source_fetched_since_the_cutoff_is_not_due()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var sourceId = await SeedSourceAsync(database);
        await ExecuteAsync(database,
            "UPDATE job_sources SET last_fetched_at = @at WHERE id = @id",
            sourceId, ("at", FetchedBefore.AddMinutes(30)));

        (await Query(database).DueSourcesAsync(Now, FetchedBefore)).ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task A_source_last_fetched_before_the_cutoff_is_due()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var sourceId = await SeedSourceAsync(database);
        await ExecuteAsync(database,
            "UPDATE job_sources SET last_fetched_at = @at WHERE id = @id",
            sourceId, ("at", FetchedBefore.AddHours(-1)));

        (await Query(database).DueSourcesAsync(Now, FetchedBefore)).ShouldHaveSingleItem().SourceId.ShouldBe(sourceId);
    }

    private static DiscoveryCycleQuery Query(TestDatabase database) =>
        new(new NpgsqlConnectionFactory(database.ConnectionString));

    private static async Task ExecuteAsync(
        TestDatabase database, string sql, Guid id, params (string Name, object Value)[] extra)
    {
        var factory = new NpgsqlConnectionFactory(database.ConnectionString);
        await using var connection = await factory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var idParam = command.CreateParameter();
        idParam.ParameterName = "id";
        idParam.Value = id;
        command.Parameters.Add(idParam);
        foreach (var (name, value) in extra)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> SeedSourceAsync(
        TestDatabase database,
        bool isActive = true,
        bool retireBinding = false,
        decimal confidence = 0.90m,
        string? compBand = null,
        bool? remoteEmeaFriendly = null)
    {
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var seenAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

        await using var ctx = database.CreateContext();
        CompBand? band = compBand is null ? null : Enum.Parse<CompBand>(compBand);
        ctx.Add(new Company(
            companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, seenAt,
            isActive: isActive, compBand: band, remoteEmeaFriendly: remoteEmeaFriendly));

        var binding = new AtsBinding(
            bindingId, companyId, AtsKind.Greenhouse, "acme",
            BindingConfidence.TryCreate(confidence).Value, "{}", seenAt);
        if (retireBinding)
        {
            binding.Retire(new FakeClock(seenAt));
        }

        ctx.Add(binding);
        ctx.Add(new JobSource(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs"));
        await ctx.SaveChangesAsync();
        return sourceId;
    }
}
