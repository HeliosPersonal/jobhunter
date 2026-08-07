using JobHunter.Domain.Reporting;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port over "the status of the active CV" (F10 <c>/cv</c>, catalogue §Profile). It answers the
/// <strong>metadata only</strong> — the active version number, when that version was activated, and how many
/// current matches were computed against it — and never the CV's content. The CV crosses exactly one boundary
/// (the F4 match prompt), and it is not this one: the implementation reads <c>version</c>, <c>activated_at</c>
/// and a <c>count</c> of current matches, and never selects <c>extracted_text</c>, which is why the F4 leakage
/// scan can leave the <c>/cv</c> path uncovered by construction rather than by an allowlist.
///
/// <para>Read-only (Dapper, architecture rule 4 forbids a write here); defined in Domain so the command handler
/// depends on the port, not the SQL. Returns null when no CV has been activated for the active profile, so
/// <c>/cv</c> can say so plainly rather than rendering a zero.</para>
/// </summary>
public interface ICvStatusQuery
{
    /// <summary>The active CV's metadata status, or null when no CV has been activated.</summary>
    Task<CvStatus?> ActiveAsync(CancellationToken cancellationToken = default);
}
