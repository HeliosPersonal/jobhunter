using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// The EF Core write repository for the company registry aggregate (data-model §companies,
/// §ats_bindings). Aggregate-scoped: companies and their bindings are saved through here, never through
/// Dapper (ADR-0003). Implements the <see cref="ICompanyRepository"/> port defined in Domain so the
/// discovery handlers depend on the port, not this type.
/// </summary>
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
