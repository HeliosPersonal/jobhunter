using JobHunter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace JobHunter.TestKit;

/// <summary>
/// One <c>postgres:17-alpine</c> container per test run behind a semaphore-gated lazy singleton, one
/// uniquely-named database per test, migrations applied on create (T06, testing strategy §3). Applying
/// migrations here means every integration test proves gate G3 (migrations apply on a clean DB). Drop
/// uses <c>WITH (FORCE)</c> so a leaked connection never blocks teardown.
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static PostgreSqlContainer? _container;
    private static int _containerStarts;

    private string _databaseName = null!;

    public string ConnectionString { get; private set; } = null!;

    /// <summary>How many times the shared container has actually been started (asserted to be 1).</summary>
    public static int ContainerStarts => Volatile.Read(ref _containerStarts);

    public static async Task<TestDatabase> CreateAsync()
    {
        var container = await EnsureContainerAsync().ConfigureAwait(false);

        var db = new TestDatabase { _databaseName = $"jh_{Guid.CreateVersion7():N}" };

        await using (var admin = new NpgsqlConnection(container.GetConnectionString()))
        {
            await admin.OpenAsync().ConfigureAwait(false);
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"""CREATE DATABASE "{db._databaseName}";""";
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        db.ConnectionString = new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = db._databaseName,
        }.ConnectionString;

        await using var ctx = JobHunterDbContextFactory.Create(db.ConnectionString);
        await ctx.Database.MigrateAsync().ConfigureAwait(false);

        return db;
    }

    /// <summary>Opens a new EF write context against this test's isolated database.</summary>
    public JobHunterDbContext CreateContext() => JobHunterDbContextFactory.Create(ConnectionString);

    private static async Task<PostgreSqlContainer> EnsureContainerAsync()
    {
        if (_container is not null)
        {
            return _container;
        }

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_container is null)
            {
                var container = new PostgreSqlBuilder("postgres:17-alpine")
                    .WithDatabase("jobhunter")
                    .WithUsername("postgres")
                    .WithPassword("postgres")
                    .Build();

                await container.StartAsync().ConfigureAwait(false);
                Interlocked.Increment(ref _containerStarts);
                _container = container;
            }
        }
        finally
        {
            Gate.Release();
        }

        return _container;
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is null)
        {
            return;
        }

        await using var admin = new NpgsqlConnection(_container.GetConnectionString());
        await admin.OpenAsync().ConfigureAwait(false);
        await using var cmd = admin.CreateCommand();
        cmd.CommandText = $"""DROP DATABASE IF EXISTS "{_databaseName}" WITH (FORCE);""";
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
