using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Sources;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Discovery;

/// <summary>
/// Upserts the company registry: the curated seed (<c>tools/seed/companies.yaml</c>) and the weekly
/// directory-expansion crawl (T03, ADR-F1-0001). Both paths are idempotent and keyed on the canonical
/// domain, so a company that already exists is left untouched and a re-run reports zero inserts.
///
/// A curated entry is trusted: it carries a known binding, so the company is created active with a
/// confident binding and an operational <see cref="JobSource"/> — local discovery produces jobs on the
/// first run. A crawled company is a proposal: it is created inactive with no binding and is never
/// activated automatically (that is detection's job, T08/T09), so a bad crawl batch can never leak into
/// the fan-out. <see cref="Company.Source"/> records provenance on every row so a bad batch is revertible.
/// </summary>
public sealed class CompanyRegistryService(
    ICompanyRepository companies,
    IJobSourceRepository sources,
    IClock clock,
    IIdGenerator ids,
    ILogger<CompanyRegistryService> logger)
{
    // A curated entry is owner-vetted, so its binding is fully confident — it clears the discovery
    // threshold and the company is activated on seed.
    private const decimal CuratedConfidence = 1.00m;
    private const string CuratedEvidence = """{"detector":"curated-seed"}""";

    private readonly ICompanyRepository _companies = companies ?? throw new ArgumentNullException(nameof(companies));
    private readonly IJobSourceRepository _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    private readonly ILogger<CompanyRegistryService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Upserts the curated seed. A domain not yet in the registry is created active with its binding and
    /// source; a domain already present is skipped so the pass is idempotent (AC: "running it twice
    /// changes nothing and reports zero inserts").
    /// </summary>
    public async Task<RegistryChange> SeedAsync(
        IReadOnlyList<CompanySeedEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var inserted = 0;
        var skipped = 0;
        var bindingsAdded = 0;

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var domain = CanonicalDomain.TryCreate(entry.Domain);
            if (domain.IsFailure)
            {
                // The loader validates every entry before we get here; an invalid domain at this point is
                // a programmer error, not an expected outcome, so it is worth surfacing loudly.
                throw new ArgumentException(
                    $"Seed entry for '{entry.Domain}' has a non-canonicalisable domain.", nameof(entries));
            }

            var existing = await _companies.FindByDomainAsync(domain.Value, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                skipped++;
                continue;
            }

            var companyId = _ids.NewId();
            var company = new Company(
                companyId, domain.Value, entry.DisplayName, CompanySource.Curated, _clock.UtcNow,
                entry.CareersUrl, entry.HqCountry, isActive: false);

            var binding = new AtsBinding(
                _ids.NewId(), companyId, entry.AtsKind, entry.BoardToken,
                BindingConfidence.TryCreate(CuratedConfidence).Value, CuratedEvidence, _clock.UtcNow);

            // A curated binding is confident, so activation always succeeds; guard anyway rather than
            // assume, because activation is the one gate that keeps an unconfident company out of the fan-out.
            var activation = company.ActivateForDiscovery([binding]);
            if (activation.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Curated company '{entry.Domain}' could not be activated: {activation.Error.Code}.");
            }

            var source = new JobSource(
                _ids.NewId(), companyId, binding.Id, AtsEndpoint.For(entry.AtsKind, entry.BoardToken));

            await _companies.AddAsync(company, cancellationToken).ConfigureAwait(false);
            await _companies.AddBindingAsync(binding, cancellationToken).ConfigureAwait(false);
            await _sources.AddAsync(source, cancellationToken).ConfigureAwait(false);

            inserted++;
            bindingsAdded++;
        }

        await _companies.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Company seed complete: {Inserted} inserted, {Skipped} already present, {Bindings} binding(s) recorded.",
            inserted, skipped, bindingsAdded);

        return new RegistryChange(inserted, skipped, bindingsAdded);
    }

    /// <summary>
    /// Records companies proposed by the weekly directory-expansion crawl. Each new domain is created
    /// inactive with no binding and is never activated automatically (ADR-F1-0001); a domain already in
    /// the registry is skipped so re-crawling converges.
    /// </summary>
    public async Task<RegistryChange> ExpandAsync(
        IReadOnlyList<CrawledCompany> crawled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crawled);

        var inserted = 0;
        var skipped = 0;

        foreach (var candidate in crawled)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var domain = CanonicalDomain.TryCreate(candidate.Domain);
            if (domain.IsFailure)
            {
                // A crawl scrapes third-party directories, so a junk domain is an expected outcome, not a
                // bug: drop it with a reason rather than aborting the whole expansion pass.
                _logger.LogInformation(
                    "Skipping crawled candidate with non-canonicalisable domain '{Domain}'.", candidate.Domain);
                skipped++;
                continue;
            }

            var existing = await _companies.FindByDomainAsync(domain.Value, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                skipped++;
                continue;
            }

            var created = Company.TryCreate(
                _ids.NewId(), domain.Value, candidate.DisplayName, CompanySource.DirectoryCrawl, _clock.UtcNow,
                candidate.CareersUrl, candidate.HqCountry, isActive: false);
            if (created.IsFailure)
            {
                _logger.LogInformation(
                    "Skipping crawled candidate '{Domain}': {Reason}.", candidate.Domain, created.Error.Code);
                skipped++;
                continue;
            }

            await _companies.AddAsync(created.Value, cancellationToken).ConfigureAwait(false);
            inserted++;
        }

        await _companies.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Directory expansion complete: {Inserted} proposed (inactive), {Skipped} skipped.", inserted, skipped);

        return new RegistryChange(inserted, skipped, 0);
    }
}
