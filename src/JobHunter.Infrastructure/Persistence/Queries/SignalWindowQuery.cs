using Dapper;
using JobHunter.Application.Preferences;
using JobHunter.Domain.Preferences;
using JobHunter.Infrastructure.Persistence.Preferences;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of the weekly refit (F7 SAD §6.1): every <see cref="Signal"/> whose reaction occurred on or
/// after a cutoff, projected into the <see cref="SignalFact"/> the pure <see cref="WeightFitter"/> consumes,
/// newest first. Implements <see cref="ISignalWindowQuery"/> with Dapper, read-only (architecture rule 4
/// forbids a write here). It reads the append-only <c>signals</c> table directly — the snapshotted
/// <c>job_facts</c> <c>jsonb</c> rehydrates through <see cref="JobFactsJson"/>, never a join to <c>jobs</c>,
/// so a later edit cannot rewrite what the Owner reacted to. It selects <strong>nothing about the Owner</strong>.
/// </summary>
public sealed class SignalWindowQuery(INpgsqlConnectionFactory connectionFactory) : ISignalWindowQuery
{
    private const string Sql =
        """
        SELECT id          AS Id,
               kind        AS Kind,
               weight      AS Weight,
               job_facts   AS JobFacts,
               occurred_at AS OccurredAt
        FROM signals
        WHERE occurred_at >= @OccurredFrom
        ORDER BY occurred_at DESC
        """;

    public async Task<IReadOnlyList<SignalFact>> LoadSince(
        DateTimeOffset occurredFrom, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(
            Sql, new { OccurredFrom = occurredFrom }, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<SignalRow>(command);

        return rows
            .Select(r => new SignalFact(
                r.Id,
                Enum.Parse<SignalKind>(r.Kind),
                r.Weight,
                JobFactsJson.Deserialize(r.JobFacts),
                r.OccurredAt))
            .ToList();
    }

    private sealed class SignalRow
    {
        public Guid Id { get; init; }

        public string Kind { get; init; } = string.Empty;

        public decimal Weight { get; init; }

        public string JobFacts { get; init; } = string.Empty;

        public DateTimeOffset OccurredAt { get; init; }
    }
}
