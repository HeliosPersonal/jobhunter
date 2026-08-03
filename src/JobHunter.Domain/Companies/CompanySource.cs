namespace JobHunter.Domain.Companies;

/// <summary>
/// How a company entered the registry (data-model §companies <c>source</c>). Persisted as <c>text</c>.
/// Provenance matters because a <see cref="DirectoryCrawl"/> discovery starts inactive and needs
/// confirmation, whereas a <see cref="Curated"/> seed is trusted.
/// </summary>
public enum CompanySource
{
    /// <summary>Seeded from the curated registry (<c>tools/seed/companies.yaml</c>).</summary>
    Curated,

    /// <summary>Found by the weekly directory-expansion crawl; created inactive pending confirmation.</summary>
    DirectoryCrawl,

    /// <summary>Added by the Owner by hand.</summary>
    Manual,
}
