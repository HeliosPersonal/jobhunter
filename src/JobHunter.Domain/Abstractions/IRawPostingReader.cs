using JobHunter.Domain.Postings;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port over stored raw postings (data-model §raw_postings). F2 normalisation and reprocessing
/// read the verbatim payload and its timestamps through here — read-only (Dapper), never a write, because
/// <c>raw_postings</c> is F1-owned and immutable (SAD §2, invariant 1). Defined in Domain so the
/// normalisation and reprocessing services depend on the port, not the Infrastructure query.
/// </summary>
public interface IRawPostingReader
{
    /// <summary>The stored posting's normalisable content, or null when the id is unknown.</summary>
    Task<RawPostingContent?> FindAsync(Guid rawPostingId, CancellationToken cancellationToken = default);
}
