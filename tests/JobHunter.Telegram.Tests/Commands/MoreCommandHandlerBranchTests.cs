using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// The composition arms of <c>/more</c>'s card view (contract §Digest and discovery): the location falls back
/// to the remote policy when no country is named, a published range is stated as fact while a board with none
/// falls back to the model's estimate marked (est), and the confidence band is a three-way summary — high,
/// med or low — or absent when the estimate carries no confidence. These are the same display facts the digest
/// renderer maps, so a below-the-cut card reads exactly like one above it; the CV is nowhere near them.
/// </summary>
public sealed class MoreCommandHandlerBranchTests
{
    private const long OwnerChat = 4242;

    private readonly IMoreCardsQuery _more = Substitute.For<IMoreCardsQuery>();

    private MoreCommandHandler NewHandler() => new(_more, NullLogger<MoreCommandHandler>.Instance);

    private void Returns(CardDisplayFacts display) =>
        _more.BelowTheCutAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new MoreCardsPage([new MoreCard(display, 64m, ["Kafka named as core"])], 1));

    private async Task<string> RenderCardAsync()
    {
        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));
        // messages[0] is the "Next 1 of 1 below the cut." header; the card is the second message.
        return messages[1].Text;
    }

    private static CardDisplayFacts Facts(
        IReadOnlyList<string> countries,
        int? publishedMin = null,
        int? publishedMax = null,
        string? publishedCurrency = null,
        int? estimatedMin = null,
        int? estimatedMax = null,
        string? estimatedCurrency = null,
        decimal? estimatedConfidence = null) => new(
        Guid.NewGuid(), "Backend Engineer, Payments", "Monzo", Stage: "Series-G", Countries: countries,
        RemotePolicy: "Remote worldwide", ApplyUrl: "https://example.test/apply",
        PublishedSalaryMin: publishedMin, PublishedSalaryMax: publishedMax, PublishedSalaryCurrency: publishedCurrency,
        EstimatedSalaryMin: estimatedMin, EstimatedSalaryMax: estimatedMax, EstimatedSalaryCurrency: estimatedCurrency,
        EstimatedSalaryConfidence: estimatedConfidence, Highlights: []);

    [Fact]
    public async Task A_card_with_no_countries_falls_back_to_the_remote_policy_for_location()
    {
        Returns(Facts(countries: [], publishedMin: 100_000, publishedMax: 130_000, publishedCurrency: "USD"));

        var card = await RenderCardAsync();

        // No country is named, so the location summary is the remote policy verbatim.
        card.ShouldContain("Remote worldwide");
    }

    [Fact]
    public async Task A_card_with_a_published_range_states_it_plainly_never_marked_as_an_estimate()
    {
        Returns(Facts(countries: ["United Kingdom"], publishedMin: 90_000, publishedMax: 115_000, publishedCurrency: "GBP"));

        var card = await RenderCardAsync();

        // A published range is fact — the salary line carries the money marker and no "(est)" qualifier.
        card.ShouldContain("💰");
        card.ShouldNotContain("est");
    }

    [Fact]
    public async Task A_card_with_no_published_range_falls_back_to_the_estimate_marked_est()
    {
        Returns(Facts(
            countries: ["United Kingdom"], estimatedMin: 80_000, estimatedMax: 100_000,
            estimatedCurrency: "GBP", estimatedConfidence: 0.6m));

        var card = await RenderCardAsync();

        // The board published no range, so the model's estimate is shown and clearly marked, never as fact.
        card.ShouldContain("(est");
    }

    [Fact]
    public async Task A_high_confidence_estimate_is_labelled_high_conf()
    {
        Returns(Facts(
            countries: ["United Kingdom"], estimatedMin: 80_000, estimatedMax: 100_000,
            estimatedCurrency: "GBP", estimatedConfidence: 0.8m));

        (await RenderCardAsync()).ShouldContain("high conf");
    }

    [Fact]
    public async Task A_mid_confidence_estimate_is_labelled_med_conf()
    {
        Returns(Facts(
            countries: ["United Kingdom"], estimatedMin: 80_000, estimatedMax: 100_000,
            estimatedCurrency: "GBP", estimatedConfidence: 0.5m));

        (await RenderCardAsync()).ShouldContain("med conf");
    }

    [Fact]
    public async Task A_low_confidence_estimate_is_labelled_low_conf()
    {
        Returns(Facts(
            countries: ["United Kingdom"], estimatedMin: 80_000, estimatedMax: 100_000,
            estimatedCurrency: "GBP", estimatedConfidence: 0.2m));

        (await RenderCardAsync()).ShouldContain("low conf");
    }

    [Fact]
    public async Task An_estimate_with_no_confidence_is_marked_est_without_a_band()
    {
        Returns(Facts(
            countries: ["United Kingdom"], estimatedMin: 80_000, estimatedMax: 100_000,
            estimatedCurrency: "GBP", estimatedConfidence: null));

        var card = await RenderCardAsync();

        // The estimate is still qualified, but with no band to state — no "conf" label follows the "(est".
        card.ShouldContain("(est");
        card.ShouldNotContain("conf");
    }

    [Fact]
    public async Task A_card_with_neither_a_published_nor_an_estimated_range_shows_no_salary_line()
    {
        Returns(Facts(countries: ["United Kingdom"]));

        var card = await RenderCardAsync();

        // Neither source has a range, so there is no salary line — only the score marker survives.
        card.ShouldNotContain("💰");
        card.ShouldContain("🎯");
    }
}
