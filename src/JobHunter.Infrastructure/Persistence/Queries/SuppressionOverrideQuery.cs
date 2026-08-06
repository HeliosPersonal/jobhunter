using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Preferences;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of the Owner's active override rules (F7 data-model §suppression_overrides, AC-06). Implements
/// <see cref="ISuppressionOverrideQuery"/> with Dapper, read-only (architecture rule 4 forbids a write here): it
/// selects every <c>suppression_overrides</c> row and rehydrates the enum-as-text <c>dimension</c> and
/// <c>mode</c> columns into the domain <see cref="SuppressionOverride"/>.
///
/// <para>Ordered by <c>(dimension, value)</c> so a re-run sees the rules in the same order (determinism). A row
/// whose stored <c>dimension</c> or <c>mode</c> is not a value this build understands is dropped rather than
/// throwing — a forward-compatible read, matching the enrichment-enum convention elsewhere. It selects
/// <strong>nothing about the Owner's CV</strong> — the CV crosses exactly one boundary, and it is not this
/// one.</para>
/// </summary>
public sealed class SuppressionOverrideQuery(INpgsqlConnectionFactory connectionFactory) : ISuppressionOverrideQuery
{
    private const string Sql =
        """
        SELECT id         AS Id,
               dimension  AS Dimension,
               value      AS Value,
               mode       AS Mode,
               created_at AS CreatedAt
        FROM suppression_overrides
        ORDER BY dimension, value
        """;

    public async Task<IReadOnlyList<SuppressionOverride>> AllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<OverrideRow>(command);

        var result = new List<SuppressionOverride>();
        foreach (var row in rows)
        {
            if (!Enum.TryParse<Dimension>(row.Dimension, ignoreCase: false, out var dimension) ||
                !Enum.TryParse<SuppressionMode>(row.Mode, ignoreCase: false, out var mode))
            {
                // A value a newer build wrote and this one does not understand: drop it rather than throw, so a
                // rolling deploy never crashes ranking on an unknown rule.
                continue;
            }

            result.Add(new SuppressionOverride(row.Id, dimension, row.Value, mode, row.CreatedAt));
        }

        return result;
    }

    private sealed record OverrideRow(
        Guid Id,
        string Dimension,
        string Value,
        string Mode,
        DateTimeOffset CreatedAt);
}
