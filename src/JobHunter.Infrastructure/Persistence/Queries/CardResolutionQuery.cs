using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of "the cards a callback short id could resolve to" (F5 T10, contract §Callback payloads).
/// Implements <see cref="ICardResolutionQuery"/> with Dapper, read-only (architecture rule 4 forbids a write
/// here): the cards of every digest generated at or after a caller-supplied cutoff, joined to <c>jobs</c>
/// for the apply URL the Open button links to. The handler HMAC-matches the callback's short id against each
/// candidate's card key to recover the job the tap applies to.
///
/// <para>The cutoff is a parameter, not a hidden limit — the Telegram layer owns the resolution window
/// through its clock — so a stale id from before the window simply returns no candidate and the caller says
/// so (AC-09). It selects <strong>nothing about the Owner</strong> — the CV crosses exactly one boundary,
/// and it is not this one.</para>
/// </summary>
public sealed class CardResolutionQuery(INpgsqlConnectionFactory connectionFactory) : ICardResolutionQuery
{
    private const string Sql =
        """
        SELECT c.card_key AS CardKey,
               c.job_id   AS JobId,
               j.apply_url AS ApplyUrl
        FROM digest_cards c
        JOIN digests d ON d.id = c.digest_id
        JOIN jobs j ON j.id = c.job_id
        WHERE d.generated_at >= @Since
        """;

    public async Task<IReadOnlyList<CardCandidate>> CandidatesSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { Since = since }, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<CandidateRow>(command);

        return rows
            .Select(r => new CardCandidate(CardKey.TryCreate(r.CardKey).Value, r.JobId, r.ApplyUrl))
            .ToList();
    }

    private sealed record CandidateRow(string CardKey, Guid JobId, string ApplyUrl);
}
