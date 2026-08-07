using JobHunter.Domain.Research;
using Microsoft.Extensions.Logging;

namespace JobHunter.Scrapers.Research;

/// <summary>
/// The reviews fetcher (SAD §5, contract §Fetcher set, risk D3). Employer reviews are fetched only from a
/// source with a usable public API or feed; no such source is configured, so the category is deliberately
/// skipped rather than scraped — see <see cref="UnconfiguredSourceFetcher"/>.
/// </summary>
internal sealed class ReviewsFetcher(ILogger<ReviewsFetcher> logger) : UnconfiguredSourceFetcher(logger)
{
    public override ResearchCategory Category => ResearchCategory.Reviews;
}
