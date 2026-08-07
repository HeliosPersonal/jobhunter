using JobHunter.Application.Abstractions;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Research;
using Microsoft.Extensions.Logging;

namespace JobHunter.Scrapers.Research;

/// <summary>
/// The interview-process fetcher (SAD §5, contract §Fetcher set): probes the company's own careers and
/// hiring-process pages — the only trustworthy public description of how it interviews is the company's own.
/// Company-scoped, so every target is bound to the company's registrable domain by the guarded allowlist.
/// </summary>
internal sealed class InterviewProcessFetcher(
    IGuardedResearchFetch fetch,
    IClock clock,
    ILogger<InterviewProcessFetcher> logger) : CompanyPageFetcher(fetch, clock, logger)
{
    public override ResearchCategory Category => ResearchCategory.InterviewProcess;

    protected override IReadOnlyList<string> Paths { get; } = ["/careers", "/interview-process", "/hiring"];
}
