using JobHunter.Domain.Companies;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The one port every ATS adapter implements (SAD §5, S1). A provider is a class behind this interface,
/// so adding a provider never changes the pipeline (QG-1). The signature is deliberately narrow: it
/// streams, so a board with 400 postings is processed with bounded memory and the 10&nbsp;MB response cap
/// is enforceable, rather than materialising the whole board first.
/// </summary>
public interface IJobSource
{
    /// <summary>The provider this adapter fetches from; the dispatcher selects an adapter by this.</summary>
    AtsKind Kind { get; }

    /// <summary>
    /// Streams every posting currently on the board named by <paramref name="binding"/>. A malformed
    /// posting inside an otherwise valid board is skipped, not thrown — acquisition of one board is one
    /// failure domain (QG-1). Transport, robots, rate and size limits are enforced by the shared handler
    /// the adapter's <see cref="System.Net.Http.HttpClient"/> is built on, never by the adapter itself.
    /// </summary>
    IAsyncEnumerable<FetchedPosting> FetchAsync(AtsBinding binding, CancellationToken cancellationToken);
}

/// <summary>
/// One posting as captured at the fetch boundary: the provider's own id, the verbatim per-posting
/// payload (immutable — invariant 1), and the content hash computed over that payload with volatile
/// fields stripped, so a cosmetic re-fetch produces the same hash (AC-02, S3).
/// </summary>
public sealed record FetchedPosting(string ExternalId, string RawPayload, string ContentHash);
