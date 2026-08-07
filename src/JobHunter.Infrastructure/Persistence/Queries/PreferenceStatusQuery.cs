using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of <c>/prefs</c> (F10 T08). Implements <see cref="IPreferenceStatusQuery"/> with Dapper,
/// read-only (architecture rule 4 forbids a write here): the latest fitted model — the highest version, which
/// is the most recent refit — its signal count and whether it is active. It answers even when no model is
/// active, which is exactly what <see cref="IPreferenceModelRepository.FindActiveAsync"/> cannot do and why the
/// below-threshold line needs a read of its own. Returns null when no model has ever been fitted, so the command
/// states the whole threshold is outstanding rather than rendering a zero.
///
/// <para>It reads only <c>version</c>, <c>signal_count</c> and <c>is_active</c> — no weight and nothing
/// CV-derived (the CV crosses exactly one boundary, and it is not this one).</para>
/// </summary>
public sealed class PreferenceStatusQuery(INpgsqlConnectionFactory connectionFactory) : IPreferenceStatusQuery
{
    private const string Sql =
        """
        SELECT
            signal_count AS SignalCount,
            is_active AS HasActiveModel
        FROM preference_models
        ORDER BY version DESC
        LIMIT 1
        """;

    public async Task<PreferenceStatus?> LatestAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, cancellationToken: cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<StatusRow>(command);

        return row is null ? null : new PreferenceStatus(row.SignalCount, row.HasActiveModel);
    }

    private sealed record StatusRow(int SignalCount, bool HasActiveModel);
}
