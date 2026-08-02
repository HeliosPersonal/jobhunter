using Npgsql;

namespace JobHunter.Infrastructure.Persistence;

/// <summary>
/// The read side's connection source (T07). Dapper queries open a short-lived connection from here,
/// sharing the exact same connection string as the EF Core write side (ADR-0003). It never exposes a
/// write path — architecture rule 4 forbids <c>ExecuteAsync</c>/<c>Execute</c> in the Queries namespace.
/// </summary>
public interface INpgsqlConnectionFactory
{
    /// <summary>Opens a new connection to the single store.</summary>
    Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class NpgsqlConnectionFactory(string connectionString) : INpgsqlConnectionFactory
{
    // The read-side chokepoint: registering the Dapper handlers here guarantees they are in place
    // before any query runs, whether the factory is created by DI or directly in a test.
    static NpgsqlConnectionFactory() => DapperTypeHandlers.EnsureRegistered();

    private readonly string _connectionString =
        !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
