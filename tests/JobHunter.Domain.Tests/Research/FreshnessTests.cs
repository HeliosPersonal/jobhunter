using JobHunter.Domain.Research;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Research;

/// <summary>
/// Freshness is a pure function of a generation time, a category and the current time (T01 done-when 3,
/// AC-06, D5). Most categories go stale at 30 days; <see cref="ResearchCategory.News"/> and
/// <see cref="ResearchCategory.Layoffs"/> — the two whose value evaporates fastest — refresh at 7
/// (SAD §8). No clock is reached for: the current time is passed in, so the policy is deterministic and
/// testable without waiting on real time.
/// </summary>
public sealed class FreshnessTests
{
    private static readonly DateTimeOffset Generated = new(2026, 1, 1, 7, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ResearchCategory.Funding, 30)]
    [InlineData(ResearchCategory.EngineeringBlog, 30)]
    [InlineData(ResearchCategory.OpenSource, 30)]
    [InlineData(ResearchCategory.Reviews, 30)]
    [InlineData(ResearchCategory.Stack, 30)]
    [InlineData(ResearchCategory.InterviewProcess, 30)]
    [InlineData(ResearchCategory.News, 7)]
    [InlineData(ResearchCategory.Layoffs, 7)]
    public void Threshold_matches_the_category(ResearchCategory category, int days)
    {
        Freshness.ThresholdFor(category).ShouldBe(TimeSpan.FromDays(days));
    }

    [Fact]
    public void A_dossier_within_the_default_window_is_fresh()
    {
        var now = Generated.AddDays(29);

        Freshness.IsStale(Generated, ResearchCategory.Funding, now).ShouldBeFalse();
    }

    [Fact]
    public void A_dossier_past_the_default_window_is_stale()
    {
        var now = Generated.AddDays(31);

        Freshness.IsStale(Generated, ResearchCategory.Funding, now).ShouldBeTrue();
    }

    [Fact]
    public void News_goes_stale_at_seven_days_where_funding_is_still_fresh()
    {
        var now = Generated.AddDays(8);

        Freshness.IsStale(Generated, ResearchCategory.News, now).ShouldBeTrue();
        Freshness.IsStale(Generated, ResearchCategory.Funding, now).ShouldBeFalse();
    }

    [Fact]
    public void Layoffs_goes_stale_at_seven_days()
    {
        Freshness.IsStale(Generated, ResearchCategory.Layoffs, Generated.AddDays(8)).ShouldBeTrue();
        Freshness.IsStale(Generated, ResearchCategory.Layoffs, Generated.AddDays(6)).ShouldBeFalse();
    }

    [Fact]
    public void Exactly_at_the_threshold_is_not_yet_stale()
    {
        // The boundary is inclusive of fresh: a dossier exactly at its threshold has not yet aged out.
        Freshness.IsStale(Generated, ResearchCategory.Funding, Generated.AddDays(30)).ShouldBeFalse();
        Freshness.IsStale(Generated, ResearchCategory.News, Generated.AddDays(7)).ShouldBeFalse();
    }

    [Fact]
    public void A_future_generation_time_is_never_stale()
    {
        // Clock skew must not manufacture staleness; a dossier generated "in the future" is trivially fresh.
        Freshness.IsStale(Generated, ResearchCategory.News, Generated.AddDays(-1)).ShouldBeFalse();
    }
}
