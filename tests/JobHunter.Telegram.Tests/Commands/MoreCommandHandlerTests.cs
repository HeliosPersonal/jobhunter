using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// <c>/more [count]</c> (contract §Digest and discovery): the next cards below today's cut, in rank order,
/// from the same stored digest — never re-ranked, so paging mid-morning keeps the ordering stable ([[PRD]]
/// §8). It reads the frozen below-the-cut set through <see cref="IMoreCardsQuery"/>, renders each through the
/// one shared card formatter (done-when 6), and reports how many remain ("Next 5 of 23 below the cut."). The
/// count argument is clamped to the catalogue's 1–20; an empty below-the-cut set is one plain, helpful line.
/// It never touches the CV (the CV crosses exactly one boundary, not this one).
/// </summary>
public sealed class MoreCommandHandlerTests
{
    private const long OwnerChat = 4242;

    private readonly IMoreCardsQuery _more = Substitute.For<IMoreCardsQuery>();

    private MoreCommandHandler NewHandler() => new(_more, NullLogger<MoreCommandHandler>.Instance);

    private static MoreCard Card(string title, decimal score, params string[] reasons) =>
        new(Facts(title), score, reasons);

    private static CardDisplayFacts Facts(string title) => new(
        Guid.NewGuid(), title, "Monzo", Stage: "Series-G", Countries: ["United Kingdom"],
        RemotePolicy: "Remote UK", ApplyUrl: "https://example.test/apply",
        PublishedSalaryMin: 90000, PublishedSalaryMax: 115000, PublishedSalaryCurrency: "GBP",
        EstimatedSalaryMin: null, EstimatedSalaryMax: null, EstimatedSalaryCurrency: null,
        EstimatedSalaryConfidence: null, Highlights: []);

    private static MoreCardsPage Page(int total, params MoreCard[] cards) => new(cards, total);

    [Fact]
    public async Task With_no_count_it_asks_for_the_default_five()
    {
        _more.BelowTheCutAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Page(0));

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        await _more.Received(1).BelowTheCutAsync(5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_count_argument_is_honoured()
    {
        _more.BelowTheCutAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Page(0));

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "8"));

        await _more.Received(1).BelowTheCutAsync(8, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_count_above_the_cap_is_clamped_to_twenty()
    {
        _more.BelowTheCutAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Page(0));

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "999"));

        await _more.Received(1).BelowTheCutAsync(20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_count_below_one_is_clamped_up_to_one()
    {
        _more.BelowTheCutAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Page(0));

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "0"));

        await _more.Received(1).BelowTheCutAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_non_numeric_count_falls_back_to_the_default_five()
    {
        _more.BelowTheCutAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Page(0));

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "lots"));

        await _more.Received(1).BelowTheCutAsync(5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task It_leads_with_a_line_reporting_how_many_are_below_the_cut()
    {
        _more.BelowTheCutAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Page(
            total: 23,
            Card("Backend Engineer, Payments", 64m, "Kafka and event sourcing named as core")));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        // "Next 1 of 23 below the cut." — the page size shown, and the whole below-the-cut total.
        messages[0].Text.ShouldContain("1");
        messages[0].Text.ShouldContain("23");
        messages[0].Text.ShouldContain("below the cut", Case.Insensitive);
    }

    [Fact]
    public async Task It_renders_each_card_through_the_shared_formatter_with_its_reasons()
    {
        _more.BelowTheCutAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Page(
            total: 2,
            Card("Backend Engineer, Payments", 64m, "Kafka and event sourcing named as core"),
            Card("Platform Engineer", 61m, "Remote within UK only")));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        // A header line, then one card message per below-the-cut role, each carrying its title, score and reason.
        messages.Count.ShouldBe(3);
        messages[1].Text.ShouldContain("Backend Engineer, Payments");
        messages[1].Text.ShouldContain("64");
        messages[1].Text.ShouldContain("Kafka and event sourcing named as core");
        messages[2].Text.ShouldContain("Platform Engineer");
    }

    [Fact]
    public async Task Nothing_below_the_cut_yields_one_plain_helpful_line()
    {
        _more.BelowTheCutAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Page(0));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages.ShouldHaveSingleItem().Text.ShouldContain("below the cut", Case.Insensitive);
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => NewHandler().HandleAsync(null!));
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        Should.Throw<ArgumentNullException>(() => new MoreCommandHandler(null!, NullLogger<MoreCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new MoreCommandHandler(_more, null!));
    }
}
