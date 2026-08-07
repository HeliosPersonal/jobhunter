namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port behind <c>/floor</c>'s preview (F10 T08, catalogue §Profile): before the Owner's explicit salary
/// floor is written, the command states how many of today's shown jobs the change <em>would have</em> affected, so
/// the Owner weighs the floor against real roles rather than a number in the abstract. "Affected" mirrors the rule
/// the ranking's suppression applies exactly: the latest Run's non-suppressed roles whose <em>high-confidence,
/// same-currency</em> estimated pay sits wholly below the proposed floor (even the top of the band misses it).
///
/// <para>Same currency only — a cross-currency verdict would be a lie; low-confidence estimates never bite, because
/// a guess cannot condemn a role (O5); a suppressed role is already withheld, so the floor does not "affect" what
/// the digest shows. Scoped to the latest Run so yesterday's below-floor set does not linger. Read-only (Dapper,
/// architecture rule 4); defined in Domain so the command handler depends on the port, not the SQL. It selects
/// <strong>nothing about the Owner's CV</strong> — the CV crosses exactly one boundary, not this one (F4 invariant).</para>
/// </summary>
public interface ISalaryFloorPreviewQuery
{
    /// <summary>
    /// Counts the latest Run's shown jobs whose high-confidence, same-currency estimated pay sits wholly below
    /// <paramref name="floor"/> in <paramref name="currency"/> — the number the <c>/floor</c> preview reports.
    /// </summary>
    Task<int> CountAffectedAsync(decimal floor, string currency, CancellationToken cancellationToken = default);
}
