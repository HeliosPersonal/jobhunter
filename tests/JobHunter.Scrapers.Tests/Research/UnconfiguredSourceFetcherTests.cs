using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Research;
using JobHunter.Scrapers.Research;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Scrapers.Tests.Research;

/// <summary>
/// The four feed categories with no public, auth-free source chosen yet — Funding, News, Layoffs, Reviews.
/// Each has a fetcher so the orchestrator dispatches over a complete set of eight (SAD §5), but with no
/// allowlisted host to contact it honestly returns no documents: absence of a source is absence of
/// information (AC-07, SAD §8 Unavailable), never a fabricated one. Reviews in particular is skipped
/// deliberately until a source with a usable public API or feed exists (T04 "Done when", risk D3).
/// </summary>
public sealed class UnconfiguredSourceFetcherTests
{
    private static readonly FakeClock Clock =
        new(new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero));

    private static Company AnyCompany() =>
        new(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            CanonicalDomain.TryCreate("acme.com").Value,
            "Acme Inc",
            CompanySource.Curated,
            Clock.UtcNow);

    public static TheoryData<IResearchFetcher, ResearchCategory> Fetchers() => new()
    {
        { new FundingFetcher(NullLogger<FundingFetcher>.Instance), ResearchCategory.Funding },
        { new NewsFeedFetcher(NullLogger<NewsFeedFetcher>.Instance), ResearchCategory.News },
        { new LayoffsFetcher(NullLogger<LayoffsFetcher>.Instance), ResearchCategory.Layoffs },
        { new ReviewsFetcher(NullLogger<ReviewsFetcher>.Instance), ResearchCategory.Reviews },
    };

    [Theory]
    [MemberData(nameof(Fetchers))]
    public async Task ReportsItsCategory_andReturnsNoDocuments(
        IResearchFetcher fetcher, ResearchCategory expected)
    {
        fetcher.Category.ShouldBe(expected);

        var docs = await fetcher.FetchAsync(AnyCompany(), CancellationToken.None);

        docs.ShouldBeEmpty();
    }
}
