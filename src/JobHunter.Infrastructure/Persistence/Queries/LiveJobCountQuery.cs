using Dapper;
using JobHunter.Domain.Abstractions;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of "how many jobs are live" (F9-T08, SAD §6.3), the authoritative side of the reconcile
/// comparison. A single <c>COUNT(*)</c> filtered on <c>status='Live'</c> so it is cheap enough to run
/// nightly, served by the partial index <c>idx_jobs_first_seen</c>. Dapper, read-only (architecture rule 4
/// forbids a write here); implements the port.
/// </summary>
public sealed class LiveJobCountQuery(INpgsqlConnectionFactory connectionFactory) : ILiveJobCounter
{
    private const string Sql = "SELECT COUNT(*) FROM jobs WHERE status = 'Live'";

    public async Task<long> CountLiveAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<long>(command);
    }
}
