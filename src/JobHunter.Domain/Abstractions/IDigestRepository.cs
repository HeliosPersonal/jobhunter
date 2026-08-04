using JobHunter.Domain.Reporting;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The write repository for the <see cref="Digest"/> aggregate and the <see cref="DigestCard"/> rows that
/// hang off it (data-model §digests/§digest_cards). The digest is assembled and persisted <em>before</em>
/// any message is sent (SAD S2), so delivery replays stored state rather than recomputing it. One digest
/// per Run is a database constraint (unique <c>run_id</c>), not a check here.
/// </summary>
public interface IDigestRepository
{
    /// <summary>Stages a digest and its cards for insert in one transaction.</summary>
    void Add(Digest digest);

    /// <summary>
    /// The digest for a Run with its cards, rank-ordered, or null. This is what delivery loads to replay a
    /// stored digest without recomputing it.
    /// </summary>
    Task<Digest?> FindByRunAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Commits the staged changes in one transaction.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
