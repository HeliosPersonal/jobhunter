using JobHunter.Domain.Companies;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The write port for the company registry aggregate (data-model §companies, §ats_bindings). EF Core,
/// aggregate-scoped: companies and their bindings are saved through here, never through Dapper
/// (ADR-0003). Discovery binds an existing company — it never creates one — so lookup by canonical
/// domain is the central read on the write side. Defined in Domain so the Application-layer discovery
/// handlers can depend on it while Infrastructure supplies the EF Core implementation.
/// </summary>
public interface ICompanyRepository
{
    Task AddAsync(Company company, CancellationToken cancellationToken = default);

    Task AddBindingAsync(AtsBinding binding, CancellationToken cancellationToken = default);

    Task<Company?> FindByDomainAsync(CanonicalDomain domain, CancellationToken cancellationToken = default);

    Task<Company?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AtsBinding?> FindBindingAsync(Guid bindingId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AtsBinding>> LiveBindingsAsync(Guid companyId, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
