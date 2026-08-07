using JobHunter.Application.Abstractions;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Research;
using Microsoft.Extensions.Logging;

namespace JobHunter.Scrapers.Research;

/// <summary>
/// The technology-stack fetcher (SAD §5, contract §Fetcher set): infers the stack from the company's own
/// public engineering pages — the engineering index, an explicit stack page, and the blog. Company-scoped,
/// so the guarded allowlist binds every target to the company's registrable domain.
/// </summary>
internal sealed class StackFetcher(
    IGuardedResearchFetch fetch,
    IClock clock,
    ILogger<StackFetcher> logger) : CompanyPageFetcher(fetch, clock, logger)
{
    public override ResearchCategory Category => ResearchCategory.Stack;

    protected override IReadOnlyList<string> Paths { get; } = ["/engineering", "/stack", "/blog"];
}
