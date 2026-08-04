using Dapper;
using JobHunter.Domain.Abstractions;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of the pre-match filter's lifecycle rule (ADR-F4-0003, T12): which of a set of jobs already
/// carry a <em>current</em> match against a given CV version. Implements <see cref="ICurrentMatchQuery"/> with
/// Dapper, read-only (architecture rule 4 forbids a write here). It selects the distinct <c>job_id</c>s among
/// the requested set whose match against <paramref name="cvVersionId"/> is still current — a match whose CV
/// version was superseded has had its <c>is_current</c> flag cleared by the re-staling sweep, so it does not
/// count and the job re-opens for matching (AC-08).
///
/// <para>It exists so the pure Application-layer filter never names the <c>matches</c> table itself: the submit
/// handler resolves this fact and hands the filter a boolean, which is what the architecture test forbidding the
/// filter from touching <c>matches</c>, <c>scores</c> or CV text relies on. An empty id set is answered without a
/// round trip.</para>
/// </summary>
public sealed class CurrentMatchQuery(INpgsqlConnectionFactory connectionFactory) : ICurrentMatchQuery
{
    private const string Sql =
        """
        SELECT DISTINCT m.job_id
        FROM matches m
        WHERE m.cv_version_id = @CvVersionId
          AND m.is_current
          AND m.job_id = ANY(@JobIds)
        """;

    public async Task<IReadOnlySet<Guid>> WithCurrentMatchAsync(
        Guid cvVersionId,
        IReadOnlyCollection<Guid> jobIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobIds);

        if (jobIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var parameters = new { CvVersionId = cvVersionId, JobIds = jobIds.ToArray() };
        var command = new CommandDefinition(Sql, parameters, cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<Guid>(command);
        return rows.ToHashSet();
    }
}
