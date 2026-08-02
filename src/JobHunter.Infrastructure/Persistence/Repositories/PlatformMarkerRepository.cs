using JobHunter.Infrastructure.Persistence.Reference;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// The reference write repository (T07): EF Core, aggregate-scoped, one save unit. Later features copy
/// this shape. Writes go through EF and the outbox; reads for projections go through Dapper queries.
/// </summary>
public interface IPlatformMarkerRepository
{
    Task AddAsync(PlatformMarker marker, CancellationToken cancellationToken = default);

    Task<PlatformMarker?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class PlatformMarkerRepository(JobHunterDbContext context) : IPlatformMarkerRepository
{
    public async Task AddAsync(PlatformMarker marker, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(marker);
        await context.Set<PlatformMarker>().AddAsync(marker, cancellationToken);
    }

    public Task<PlatformMarker?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Set<PlatformMarker>().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
