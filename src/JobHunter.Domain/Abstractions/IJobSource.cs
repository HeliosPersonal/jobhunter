using JobHunter.Domain.Companies;
using JobHunter.Domain.Sources;

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
    /// This is the sampling convenience detection uses (it reads the head of a board and stops); the
    /// terminal fetch outcome is discarded, so discovery uses <see cref="FetchBoardAsync"/> instead.
    /// </summary>
    IAsyncEnumerable<FetchedPosting> FetchAsync(AtsBinding binding, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches the board named by <paramref name="binding"/> and reports the <em>terminal outcome</em>
    /// (success, rate-limited, refused, HTTP error, transport failure) alongside the streamed postings.
    /// Discovery needs the outcome to log every attempt (AC-11), quarantine on repeated failure (AC-08)
    /// and requeue on a rate deferral (AC-07) — an empty stream alone cannot tell an empty board from a
    /// 500. The postings still stream lazily, so a 400-posting board is ingested with bounded memory.
    /// </summary>
    Task<SourceFetch> FetchBoardAsync(AtsBinding binding, CancellationToken cancellationToken);
}

/// <summary>
/// One posting as captured at the fetch boundary: the provider's own id, the verbatim per-posting
/// payload (immutable — invariant 1), and the content hash computed over that payload with volatile
/// fields stripped, so a cosmetic re-fetch produces the same hash (AC-02, S3).
/// </summary>
public sealed record FetchedPosting(string ExternalId, string RawPayload, string ContentHash);

/// <summary>
/// The result of one board fetch: the classified terminal outcome and the (lazily streamed) postings.
/// A value, not an exception — an HTTP error or a rate deferral is an expected business outcome the
/// discovery handler logs and reacts to, never a fault it throws through (coding-standards §1).
/// </summary>
public sealed record SourceFetch(
    FetchOutcome Outcome,
    short HttpStatus,
    IAsyncEnumerable<FetchedPosting> Postings,
    TimeSpan? RetryAfter = null,
    string? Detail = null)
{
    /// <summary>True when the board answered and its payload parsed — postings may still be empty.</summary>
    public bool IsSuccess => Outcome == FetchOutcome.Success;
}
