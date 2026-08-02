using JobHunter.Domain.Companies;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// The write repository for the company registry aggregate (data-model §companies, §ats_bindings). EF
/// Core, aggregate-scoped: companies and their bindings are saved through here, never through Dapper
/// (ADR-0003). Discovery binds an existing company — it never creates one — so lookup by canonical
/// domain is the central read on the write side.
/// </summary>
public interface ICompanyRepository
{
    Task AddAsync(Company company, CancellationToken cancellationToken = default);

    Task AddBindingAsync(AtsBinding binding, CancellationToken cancellationToken = default);

    Task<Company?> FindByDomainAsync(CanonicalDomain domain, CancellationToken cancellationToken = default);

    Task<Company?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AtsBinding>> LiveBindingsAsync(Guid companyId, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class CompanyRepository(JobHunterDbContext context) : ICompanyRepository
{
    public async Task AddAsync(Company company, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(company);
        await context.Set<Company>().AddAsync(company, cancellationToken);
    }

    public async Task AddBindingAsync(AtsBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        await context.Set<AtsBinding>().AddAsync(binding, cancellationToken);
    }

    public Task<Company?> FindByDomainAsync(CanonicalDomain domain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return context.Set<Company>().FirstOrDefaultAsync(c => c.CanonicalDomain == domain, cancellationToken);
    }

    public Task<Company?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Set<Company>().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AtsBinding>> LiveBindingsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var bindings = await context.Set<AtsBinding>()
            .Where(x => x.CompanyId == companyId && x.RetiredAt == null)
            .ToListAsync(cancellationToken);
        return bindings;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
