using JobHunter.Domain.Reporting;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port behind <c>/more</c> (F10 T06, catalogue §Digest and discovery): the roles the latest Run
/// showed but did not card — scored at or above the card threshold and not suppressed, yet ranked outside
/// the digest's top cards. Read-only (Dapper, architecture rule 4); defined in Domain so the command handler
/// depends on the port, not the SQL.
///
/// <para>It reads the <em>frozen</em> stored scores in their original order rather than re-ranking, so
/// paging through <c>/more</c> mid-morning keeps the ordering stable ([[PRD]] §8). The definition of "the
/// cut" — how many top cards the digest already carried, and the score threshold a card must clear — belongs
/// to the read side, so the handler asks only for how many to show. It selects <strong>nothing about the
/// Owner's CV</strong> — the CV crosses exactly one boundary, not this one (F4 invariant).</para>
/// </summary>
public interface IMoreCardsQuery
{
    /// <summary>The next <paramref name="take"/> roles below today's cut, best-score first, with the total below the cut.</summary>
    Task<MoreCardsPage> BelowTheCutAsync(int take, CancellationToken cancellationToken = default);
}
