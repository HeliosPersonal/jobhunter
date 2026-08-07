using JobHunter.Domain.Research;
using Microsoft.Extensions.Logging;

namespace JobHunter.Scrapers.Research;

/// <summary>
/// The layoffs fetcher (SAD §5, contract §Fetcher set). No public layoff-tracker feed is configured yet,
/// so the warning category is honestly unavailable until one is chosen and allowlisted — see
/// <see cref="UnconfiguredSourceFetcher"/>.
/// </summary>
internal sealed class LayoffsFetcher(ILogger<LayoffsFetcher> logger) : UnconfiguredSourceFetcher(logger)
{
    public override ResearchCategory Category => ResearchCategory.Layoffs;
}
