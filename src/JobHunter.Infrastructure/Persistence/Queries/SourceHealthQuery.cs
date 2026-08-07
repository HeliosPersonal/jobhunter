using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Sources;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of <c>/sources</c> (F10 T09, R4): rolls up <c>source_fetch_log</c> by ATS provider over a
/// trailing window — attempts, successes and the last attempt — so the operations reply shows which integration
/// is failing at a glance. Dapper, flat DTO, read-only (architecture rule 4 forbids a write here); implements the
/// Domain port.
///
/// <para>The provider is not stored on the log row, so the roll-up joins each attempt to its source's ATS binding
/// (<c>source_fetch_log → job_sources → ats_bindings</c>) and groups on <c>ats_kind</c> — every source a provider
/// backs collapses into one line. <c>outcome</c> is the persisted enum name, so a success is <c>'Success'</c> and
/// every other outcome (rate-limited, robots-denied, HTTP, transport, parse, quarantined) is a failed attempt.
/// Only attempts at or after <c>@Since</c> are counted, so an old failure does not follow the provider forever.</para>
/// </summary>
public sealed class SourceHealthQuery(INpgsqlConnectionFactory connectionFactory) : ISourceHealthQuery
{
    private const string Sql =
        """
        SELECT b.ats_kind AS AtsKind,
               count(*)::int AS Attempts,
               count(*) FILTER (WHERE l.outcome = 'Success')::int AS Successes,
               max(l.started_at) AS LastAttemptAt
        FROM source_fetch_log l
        JOIN job_sources s ON s.id = l.source_id
        JOIN ats_bindings b ON b.id = s.binding_id
        WHERE l.started_at >= @Since
        GROUP BY b.ats_kind
        ORDER BY b.ats_kind
        """;

    public async Task<IReadOnlyList<SourceHealth>> HealthSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { Since = since }, cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<SourceHealth>(command);
        return rows.AsList();
    }
}
