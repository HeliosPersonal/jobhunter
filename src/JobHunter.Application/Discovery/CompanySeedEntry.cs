using JobHunter.Domain.Companies;

namespace JobHunter.Application.Discovery;

/// <summary>
/// One curated registry row as read from <c>tools/seed/companies.yaml</c> (T03, ADR-F1-0001). A curated
/// entry is trusted, so it carries a known ATS binding — the provider and board token — which lets local
/// discovery produce jobs on the first run rather than waiting for a detection pass. The provenance
/// (<see cref="CompanySource.Curated"/>) is stamped by <see cref="CompanyRegistryService"/>, not here.
/// </summary>
public sealed record CompanySeedEntry(
    string Domain,
    string DisplayName,
    AtsKind AtsKind,
    string BoardToken,
    string? CareersUrl = null,
    string? HqCountry = null);

/// <summary>
/// A company proposed by the weekly directory-expansion crawl (T03, ADR-F1-0001). It has no trusted
/// binding yet — detection (T08/T09) confirms one later — so it is inserted inactive and is never
/// activated automatically.
/// </summary>
public sealed record CrawledCompany(
    string Domain,
    string DisplayName,
    string? CareersUrl = null,
    string? HqCountry = null);

/// <summary>
/// The outcome of a registry upsert pass. <see cref="Inserted"/> is the count of new companies, so a
/// second identical seed run reports zero (the T03 idempotency guarantee). <see cref="BindingsAdded"/>
/// counts bindings recorded — for new companies and for a previously crawled company promoted by curation.
/// </summary>
public sealed record RegistryChange(int Inserted, int Skipped, int BindingsAdded)
{
    public static readonly RegistryChange None = new(0, 0, 0);
}
