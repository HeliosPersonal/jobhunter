using JobHunter.Domain.Reporting;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port the production digest renderer draws a card's <em>display</em> facts from (F5 T12). A
/// <c>DigestCard</c> snapshots the score and reasons it was assembled with (invariant 4), but not the title,
/// company, location or salary the card shows — those are the job's own, joined at render time so a
/// re-rendered <c>/digest</c> reflects the job as it stands. This port returns those facts for a batch of job
/// ids in one round-trip, keyed by job id: a job with no row is simply absent from the result rather than a
/// fabricated card. Read-only (Dapper, architecture rule 4); defined in Domain so the renderer depends on the
/// port, not the SQL.
///
/// <para>It carries the published salary and — as the fallback the card marks <c>(est)</c> — the job's most
/// recent enrichment estimate. It selects <strong>nothing about the Owner</strong>: the CV crosses exactly
/// one boundary, and it is not this one (F4 invariant).</para>
/// </summary>
public interface ICardDisplayQuery
{
    /// <summary>
    /// The display facts for each of <paramref name="jobIds"/> that exists, keyed by job id. A job id with no
    /// stored row is omitted from the dictionary.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, CardDisplayFacts>> DisplayFactsAsync(
        IReadOnlyCollection<Guid> jobIds, CancellationToken cancellationToken = default);
}
