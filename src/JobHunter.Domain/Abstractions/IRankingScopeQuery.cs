using JobHunter.Domain.Intelligence;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port over the current matches a Run's ranking pass scores (F4 SAD §6.2, data-model §matches/§scores).
/// Returns one <see cref="RankingJob"/> per <em>current</em> match in the Run — the model's fit judgement joined
/// to the job's first-seen timestamp and its latest enrichment — so <c>ScoreCalculator</c> has every explicit
/// input it needs without the handler assembling it from three repositories. Read-only (Dapper), defined in
/// Domain so the Application ranking handler depends on the port, not the Infrastructure query.
///
/// <para>Scoped to the Run's matches keyed on <c>(run_id)</c> and <c>is_current</c>: a stale match superseded by
/// a CV re-upload mid-Run is not ranked. The enrichment attached is the latest for the job (any Run), or absent
/// when the job has none, so a matched-but-unenriched job is still ranked at a discounted confidence rather than
/// dropped (AC-09). Deterministically ordered by job id so a re-run sees the same items in the same order. It
/// selects <strong>nothing about the Owner</strong> — the CV crosses exactly one boundary, F4's match prompt.</para>
/// </summary>
public interface IRankingScopeQuery
{
    /// <summary>
    /// The current matched jobs of <paramref name="runId"/>, each with its match score, first-seen timestamp,
    /// whether an enrichment backs it, and the enrichment's estimated pay. Ordered by job id.
    /// </summary>
    Task<IReadOnlyList<RankingJob>> InScopeAsync(Guid runId, CancellationToken cancellationToken = default);
}
