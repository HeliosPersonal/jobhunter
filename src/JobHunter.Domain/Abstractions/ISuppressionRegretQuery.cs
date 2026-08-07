namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port behind the suppression-regret metric (F7 T09 done-when 5, risk D3): how many of the latest
/// Run's suppressed jobs the Owner then acted on — retrieved through <c>/hidden</c> and opened, saved or
/// applied to. It is the counterweight to precision@10: precision asks whether what was shown was wanted,
/// regret asks whether what was hidden was wanted after all. A non-zero, rising regret is the signal that the
/// learned model is over-suppressing (invariant 11). Read-only (Dapper, architecture rule 4); defined in
/// Domain so the reporter depends on the port, not the SQL.
///
/// <para>Scoped to the latest Run so a since-superseded suppression does not linger in the number. It selects
/// <strong>nothing about the Owner's CV</strong> — the CV crosses exactly one boundary, not this one.</para>
/// </summary>
public interface ISuppressionRegretQuery
{
    /// <summary>The count of the latest Run's suppressed jobs the Owner acted on; zero when there is none.</summary>
    Task<int> LatestRunRegretCountAsync(CancellationToken cancellationToken = default);
}
