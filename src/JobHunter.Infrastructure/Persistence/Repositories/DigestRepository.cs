using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// The write repository for the <see cref="Digest"/> aggregate and its cards (data-model §digests). The
/// digest is assembled and persisted <em>before</em> any message is sent (SAD S2); one digest per Run is a
/// database constraint (<c>uq_digests_run</c>), so a second assembly for the same Run fails at commit rather
/// than duplicating. The cards are written through the same insert as owned children.
/// </summary>
public sealed class DigestRepository(JobHunterDbContext context) : IDigestRepository
{
    public void Add(Digest digest)
    {
        ArgumentNullException.ThrowIfNull(digest);
        context.Set<Digest>().Add(digest);
    }

    public Task<Digest?> FindByRunAsync(Guid runId, CancellationToken cancellationToken = default) =>
        context.Set<Digest>()
            .Include(d => d.Cards)
            .FirstOrDefaultAsync(d => d.RunId == runId, cancellationToken);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
