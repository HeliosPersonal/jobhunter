using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Research;
using Microsoft.Extensions.Logging;

namespace JobHunter.Scrapers.Research;

/// <summary>
/// The shared shape of the four feed categories with no public, auth-free source chosen yet — Funding,
/// News, Layoffs and Reviews (contract §Fetcher set marks each "specific allowlisted hosts", none of which
/// is configured; <see cref="Infrastructure"/>'s allowlist denies them by default). Each still exists as a
/// fetcher so the orchestrator dispatches over a complete set of eight categories (SAD §5), but with no
/// host to contact it returns no documents — absence of a source is recorded as the category being
/// unavailable, which is information, not a gap to fill from memory (AC-07, SAD §8 Unavailable, QG-2).
///
/// <para>This is the honest placeholder the design sanctions rather than an unfinished stub: Reviews is
/// explicitly to be skipped until a source with a usable public API or feed exists (risk D3), and the same
/// deny-by-default posture is correct for the others until their feeds are chosen. Adding a real source
/// later is a new fetcher plus an allowlist entry — never a change to the pipeline.</para>
/// </summary>
internal abstract class UnconfiguredSourceFetcher(ILogger logger) : IResearchFetcher
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public abstract ResearchCategory Category { get; }

    public Task<IReadOnlyList<FetchedDocument>> FetchAsync(
        Company company,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(company);

        _logger.LogInformation(
            "No public source configured for {Category}; recording it as unavailable for {Company}",
            Category, company.CanonicalDomain.Value);

        return Task.FromResult<IReadOnlyList<FetchedDocument>>([]);
    }
}
