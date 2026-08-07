using JobHunter.Domain.Research;
using Microsoft.Extensions.Logging;

namespace JobHunter.Scrapers.Research;

/// <summary>
/// The news fetcher (SAD §5, contract §Fetcher set). No public news search feed is configured yet, so the
/// category is honestly unavailable until one is chosen and allowlisted — see
/// <see cref="UnconfiguredSourceFetcher"/>.
/// </summary>
internal sealed class NewsFeedFetcher(ILogger<NewsFeedFetcher> logger) : UnconfiguredSourceFetcher(logger)
{
    public override ResearchCategory Category => ResearchCategory.News;
}
