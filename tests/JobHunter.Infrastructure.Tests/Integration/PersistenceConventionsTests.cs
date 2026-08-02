using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Reference;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// T07: the EF write repository and the Dapper read query are one worked example each. A marker written
/// through the repository is read back through the query, proving the two sides share a store and a
/// connection string. Requires Docker.
/// </summary>
public sealed class PersistenceConventionsTests
{
    [RequiresDockerFact]
    public async Task A_marker_written_through_the_repository_is_read_by_the_dapper_query()
    {
        await using var database = await TestDatabase.CreateAsync();
        var id = Guid.CreateVersion7();
        var recordedAt = new DateTimeOffset(2026, 8, 2, 6, 30, 0, TimeSpan.Zero);

        await using (var context = database.CreateContext())
        {
            var repository = new PlatformMarkerRepository(context);
            await repository.AddAsync(new PlatformMarker(id, "active-one", MarkerStatus.Active, recordedAt));
            await repository.SaveChangesAsync();
        }

        var query = new PlatformMarkerQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var rows = await query.ActiveMarkersAsync();

        var row = rows.ShouldHaveSingleItem();
        row.Id.ShouldBe(id);
        row.Label.ShouldBe("active-one");
        row.Status.ShouldBe("Active");
    }

    [RequiresDockerFact]
    public async Task The_query_returns_only_active_markers()
    {
        await using var database = await TestDatabase.CreateAsync();

        await using (var context = database.CreateContext())
        {
            var repository = new PlatformMarkerRepository(context);
            await repository.AddAsync(new PlatformMarker(
                Guid.CreateVersion7(), "pending-one", MarkerStatus.Pending, DateTimeOffset.UtcNow));
            await repository.AddAsync(new PlatformMarker(
                Guid.CreateVersion7(), "active-one", MarkerStatus.Active, DateTimeOffset.UtcNow));
            await repository.SaveChangesAsync();
        }

        var query = new PlatformMarkerQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var rows = await query.ActiveMarkersAsync();

        rows.ShouldHaveSingleItem().Label.ShouldBe("active-one");
    }

    [RequiresDockerFact]
    public async Task FindAsync_returns_null_for_an_unknown_id()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var repository = new PlatformMarkerRepository(context);

        (await repository.FindAsync(Guid.CreateVersion7())).ShouldBeNull();
    }

    [RequiresDockerFact]
    public async Task The_unique_label_index_rejects_a_duplicate()
    {
        await using var database = await TestDatabase.CreateAsync();

        await using var context = database.CreateContext();
        var repository = new PlatformMarkerRepository(context);
        await repository.AddAsync(new PlatformMarker(
            Guid.CreateVersion7(), "dup", MarkerStatus.Pending, DateTimeOffset.UtcNow));
        await repository.AddAsync(new PlatformMarker(
            Guid.CreateVersion7(), "dup", MarkerStatus.Pending, DateTimeOffset.UtcNow));

        await Should.ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(
            () => repository.SaveChangesAsync());
    }
}
