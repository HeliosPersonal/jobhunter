using JobHunter.Application.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Research;
using JobHunter.Scrapers.Research;
using JobHunter.Scrapers.Tests.Support;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Scrapers.Tests.Research;

/// <summary>
/// The company-scoped page fetchers (SAD §5): engineering blog, stack and interview process each probe a
/// fixed set of paths on the company's own domain through the guarded fetch path, keep the pages that
/// yield extractable text, and isolate every other outcome (refused, error, empty, a dead candidate) as
/// simply no document — a fetcher never throws and one dead candidate never suppresses the others (AC-07).
/// </summary>
public sealed class CompanyPageFetcherTests
{
    private static readonly FakeClock Clock =
        new(new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero));

    private static Company CompanyOn(string domain)
    {
        var canonical = CanonicalDomain.TryCreate(domain).Value;
        return new Company(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            canonical,
            "Acme Inc",
            CompanySource.Curated,
            Clock.UtcNow);
    }

    [Fact]
    public async Task EngineeringBlog_probesBlogEngineeringAndEngPaths_onTheCompanyDomain()
    {
        var guard = FakeGuardedResearchFetch.NothingFound();
        var fetcher = new EngineeringBlogFetcher(guard, Clock, NullLogger<EngineeringBlogFetcher>.Instance);

        await fetcher.FetchAsync(CompanyOn("acme.com"), CancellationToken.None);

        fetcher.Category.ShouldBe(ResearchCategory.EngineeringBlog);
        guard.Calls.Select(c => c.Url.ToString()).ShouldBe(
        [
            "https://acme.com/blog",
            "https://acme.com/engineering",
            "https://acme.com/eng",
        ]);
        guard.Calls.ShouldAllBe(c => c.Category == ResearchCategory.EngineeringBlog && c.CompanyDomain == "acme.com");
    }

    [Fact]
    public async Task APageWithExtractableText_becomesADocument_withItsFinalUrlAndObservedTime()
    {
        var guard = FakeGuardedResearchFetch.ServingHtml(new Dictionary<string, string>
        {
            ["https://acme.com/blog"] = "<html><head><title>Acme Engineering</title></head>"
                + "<body><h1>How we ship</h1><p>We deploy many times a day.</p></body></html>",
        });
        var fetcher = new EngineeringBlogFetcher(guard, Clock, NullLogger<EngineeringBlogFetcher>.Instance);

        var docs = await fetcher.FetchAsync(CompanyOn("acme.com"), CancellationToken.None);

        var doc = docs.ShouldHaveSingleItem();
        doc.Url.ShouldBe("https://acme.com/blog");
        doc.Title.ShouldBe("Acme Engineering");
        doc.Text.ShouldContain("How we ship");
        doc.Text.ShouldContain("We deploy many times a day.");
        doc.ObservedAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public async Task APageWithNoExtractableText_isNotADocument()
    {
        var guard = FakeGuardedResearchFetch.ServingHtml(new Dictionary<string, string>
        {
            ["https://acme.com/blog"] = "<html><body><script>renderApp();</script></body></html>",
        });
        var fetcher = new EngineeringBlogFetcher(guard, Clock, NullLogger<EngineeringBlogFetcher>.Instance);

        var docs = await fetcher.FetchAsync(CompanyOn("acme.com"), CancellationToken.None);

        docs.ShouldBeEmpty();
    }

    [Fact]
    public async Task ARefusedTarget_isNoDocument_butWasStillAttemptedThroughTheGuard()
    {
        var guard = new FakeGuardedResearchFetch(_ => ResearchFetchResult.Refused("host-not-allowlisted"));
        var fetcher = new EngineeringBlogFetcher(guard, Clock, NullLogger<EngineeringBlogFetcher>.Instance);

        var docs = await fetcher.FetchAsync(CompanyOn("acme.com"), CancellationToken.None);

        docs.ShouldBeEmpty();
        guard.Calls.ShouldNotBeEmpty(); // the guard, not the fetcher, decides refusal
    }

    [Fact]
    public async Task ADeadCandidate_thatThrows_isIsolated_andTheOthersStillProduceDocuments()
    {
        var guard = FakeGuardedResearchFetch.ServingHtml(new Dictionary<string, string>
        {
            ["https://acme.com/engineering"] = "<p>We run on .NET and Postgres.</p>",
        });
        guard.Throw = new Uri("https://acme.com/blog"); // first candidate blows up

        var fetcher = new EngineeringBlogFetcher(guard, Clock, NullLogger<EngineeringBlogFetcher>.Instance);

        var docs = await fetcher.FetchAsync(CompanyOn("acme.com"), CancellationToken.None);

        var doc = docs.ShouldHaveSingleItem();
        doc.Url.ShouldBe("https://acme.com/engineering");
    }

    [Fact]
    public async Task InterviewProcess_probesCareersAndProcessPaths_onTheCompanyDomain()
    {
        var guard = FakeGuardedResearchFetch.NothingFound();
        var fetcher = new InterviewProcessFetcher(guard, Clock, NullLogger<InterviewProcessFetcher>.Instance);

        await fetcher.FetchAsync(CompanyOn("acme.com"), CancellationToken.None);

        fetcher.Category.ShouldBe(ResearchCategory.InterviewProcess);
        guard.Calls.Select(c => c.Url.ToString()).ShouldBe(
        [
            "https://acme.com/careers",
            "https://acme.com/interview-process",
            "https://acme.com/hiring",
        ]);
    }

    [Fact]
    public async Task Stack_probesStackEngineeringAndBlogPaths_onTheCompanyDomain()
    {
        var guard = FakeGuardedResearchFetch.NothingFound();
        var fetcher = new StackFetcher(guard, Clock, NullLogger<StackFetcher>.Instance);

        await fetcher.FetchAsync(CompanyOn("acme.com"), CancellationToken.None);

        fetcher.Category.ShouldBe(ResearchCategory.Stack);
        guard.Calls.Select(c => c.Url.ToString()).ShouldBe(
        [
            "https://acme.com/engineering",
            "https://acme.com/stack",
            "https://acme.com/blog",
        ]);
    }
}
