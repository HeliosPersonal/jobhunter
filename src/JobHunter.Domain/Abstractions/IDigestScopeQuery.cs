using JobHunter.Domain.Reporting;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port over the scores a Run's digest assembly draws on (F5 SAD §6.1, data-model §scores/§matches).
/// Returns one <see cref="DigestCandidate"/> per score in the Run — shown or suppressed — joined to its
/// current match's reasons and salary expectation, so the assembler has the card explanation and the salary
/// input without touching three tables itself. Read-only (Dapper); defined in Domain so the assembler depends
/// on the port, not the SQL.
///
/// <para>It returns <em>every</em> score, not only the shown ones: the suppression breakdown is built from the
/// suppressed candidates (invariant 11, AC-07), and a silent filter here would make the footer a lie. Ordered
/// by final score descending then job id, so card selection and the top-ten cut are deterministic (QG-3). It
/// selects <strong>nothing about the Owner</strong> — the CV crosses exactly one boundary, and it is not this
/// one (F4 invariant).</para>
/// </summary>
public interface IDigestScopeQuery
{
    /// <summary>
    /// The Run's scored candidates, each with its final score, suppression verdict, current-match reasons and
    /// USD salary (when USD-denominated). Ordered by final score descending, then job id.
    /// </summary>
    Task<IReadOnlyList<DigestCandidate>> CandidatesAsync(Guid runId, CancellationToken cancellationToken = default);
}
