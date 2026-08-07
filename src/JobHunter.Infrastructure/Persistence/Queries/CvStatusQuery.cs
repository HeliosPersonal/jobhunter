using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of <c>/cv</c> (F10 T08). Implements <see cref="ICvStatusQuery"/> with Dapper, read-only
/// (architecture rule 4 forbids a write here): the active profile's active CV version, when it was activated,
/// and how many <em>current</em> matches were computed against it — a match whose CV was superseded has had its
/// <c>is_current</c> flag cleared and so does not count, which keeps the number the Owner sees the true
/// matched-against total.
///
/// <para>The SQL selects <c>version</c>, <c>activated_at</c> and a <c>count</c> and <strong>never
/// <c>extracted_text</c></strong>: the CV crosses exactly one boundary (the F4 match prompt) and it is not this
/// one, which is why the F4 leakage scan can leave this path uncovered by construction rather than by an
/// allowlist. Returns null when no CV has been activated, so the command says so plainly rather than rendering a
/// zero.</para>
/// </summary>
public sealed class CvStatusQuery(INpgsqlConnectionFactory connectionFactory) : ICvStatusQuery
{
    private const string Sql =
        """
        SELECT
            c.version AS Version,
            c.activated_at AS ActivatedAt,
            (SELECT count(*)::int FROM matches m
                WHERE m.cv_version_id = c.id AND m.is_current) AS MatchCount
        FROM cv_versions c
        JOIN profiles p ON p.id = c.profile_id
        WHERE p.is_active AND c.is_active
        LIMIT 1
        """;

    public async Task<CvStatus?> ActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, cancellationToken: cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<StatusRow>(command);

        return row is null ? null : new CvStatus(row.Version, row.ActivatedAt, row.MatchCount);
    }

    private sealed record StatusRow(short Version, DateTimeOffset? ActivatedAt, int MatchCount);
}
