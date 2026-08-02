using Dapper;
using JobHunter.Infrastructure.Persistence.Reference;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The reference read query (T07): Dapper, flat DTO, hand-written SQL, read-only. Every type in this
/// namespace is forbidden from calling <c>ExecuteAsync</c>/<c>Execute</c> — architecture rule 4 asserts
/// it, encoding "Dapper never writes" (ADR-0003). DTOs are <c>record</c> types local to the query file,
/// never shared models.
/// </summary>
public sealed class PlatformMarkerQuery(INpgsqlConnectionFactory connectionFactory)
{
    public async Task<IReadOnlyList<PlatformMarkerRow>> ActiveMarkersAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(
            """
            SELECT id AS Id, label AS Label, status AS Status, recorded_at AS RecordedAt
            FROM platform_markers
            WHERE status = @Status
            ORDER BY recorded_at DESC
            """,
            new { Status = nameof(MarkerStatus.Active) },
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<PlatformMarkerRow>(command);
        return rows.AsList();
    }
}

/// <summary>A flat read DTO — not the aggregate.</summary>
public sealed record PlatformMarkerRow(Guid Id, string Label, string Status, DateTimeOffset RecordedAt);
