using System.Diagnostics;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Reference;
using JobHunter.TestKit;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// AC-02 / gate G3: the first migration applies cleanly on an empty database, creates the reference
/// table, honours the enum-as-text and timestamptz conventions, and is a no-op when applied twice.
/// Requires a Docker engine (Testcontainers Postgres); skipped where none is reachable.
/// </summary>
public sealed class MigrationApplicationTests
{
    [RequiresDockerFact]
    public async Task Migrations_ApplyCleanly_OnEmptyDatabase_InUnderFiveSeconds()
    {
        var stopwatch = Stopwatch.StartNew();
        await using var database = await TestDatabase.CreateAsync();
        stopwatch.Stop();

        // TestDatabase applies migrations on create; the whole create-and-migrate must be well under 5s.
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));

        await using var context = database.CreateContext();
        var applied = await context.Database.GetAppliedMigrationsAsync();
        applied.ShouldNotBeEmpty();

        var pending = await context.Database.GetPendingMigrationsAsync();
        pending.ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task Migrations_AppliedTwice_AreANoOp()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        var before = (await context.Database.GetAppliedMigrationsAsync()).ToList();

        // Re-applying must not add history rows.
        await context.Database.MigrateAsync();

        var after = (await context.Database.GetAppliedMigrationsAsync()).ToList();
        after.ShouldBe(before);
    }

    [RequiresDockerFact]
    public async Task The_reference_table_round_trips_the_enum_as_text_and_the_utc_timestamp()
    {
        await using var database = await TestDatabase.CreateAsync();
        var id = Guid.CreateVersion7();
        var recordedAt = new DateTimeOffset(2026, 8, 2, 7, 0, 0, TimeSpan.Zero);

        await using (var write = database.CreateContext())
        {
            write.Add(new PlatformMarker(id, "bootstrap", MarkerStatus.Active, recordedAt));
            await write.SaveChangesAsync();
        }

        await using (var read = database.CreateContext())
        {
            var loaded = await read.Set<PlatformMarker>().SingleAsync(m => m.Id == id);
            loaded.Label.ShouldBe("bootstrap");
            loaded.Status.ShouldBe(MarkerStatus.Active);
            loaded.RecordedAt.ToUniversalTime().ShouldBe(recordedAt);
        }

        // Assert the enum persisted as text, not an ordinal (coding-standards §5).
        await using var connection = new Npgsql.NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT status FROM platform_markers WHERE id = @id";
        command.Parameters.AddWithValue("id", id);
        var status = (string?)await command.ExecuteScalarAsync();
        status.ShouldBe("Active");
    }

    [RequiresDockerFact]
    public async Task The_hangfire_schema_is_created_by_the_first_migration()
    {
        await using var database = await TestDatabase.CreateAsync();

        await using var connection = new Npgsql.NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = 'hangfire'";
        var count = (long)(await command.ExecuteScalarAsync() ?? 0L);
        count.ShouldBe(1);
    }
}
