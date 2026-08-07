using JobHunter.Application.Abstractions;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Research;
using Microsoft.Extensions.Logging;

namespace JobHunter.Scrapers.Research;

/// <summary>
/// The engineering-blog fetcher (SAD §5, contract §Fetcher set): probes the conventional blog paths on the
/// company's own domain — the evidence for an engineering culture that writes lives there, not on a third
/// party. Company-scoped, so the guarded allowlist binds every target to this company's registrable domain.
/// </summary>
internal sealed class EngineeringBlogFetcher(
    IGuardedResearchFetch fetch,
    IClock clock,
    ILogger<EngineeringBlogFetcher> logger) : CompanyPageFetcher(fetch, clock, logger)
{
    public override ResearchCategory Category => ResearchCategory.EngineeringBlog;

    protected override IReadOnlyList<string> Paths { get; } = ["/blog", "/engineering", "/eng"];
}
