using Dapper;
using JobHunter.Domain.Abstractions;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of "how many companies is the pipeline scanning" (AC-05): a count of the active company
/// registry, served by <c>idx_companies_active</c> (the partial index on <c>is_active</c>). Dapper,
/// read-only (architecture rule 4 forbids a write here); implements the Domain port. It is read once at
/// assembly for the <c>NothingNew</c> header and snapshotted onto the digest, so a later change in the
/// registry cannot rewrite a delivered digest's stated scope.
/// </summary>
public sealed class ActiveCompanyCountQuery(INpgsqlConnectionFactory connectionFactory) : IActiveCompanyCountQuery
{
    private const string Sql = "SELECT COUNT(*)::int FROM companies WHERE is_active";

    public async Task<int> ActiveCompanyCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<int>(command);
    }
}
