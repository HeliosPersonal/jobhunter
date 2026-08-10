using JobHunter.Application.Ratings;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;
using DeliveryOptions = JobHunter.Application.Delivery.DeliveryOptions;

namespace JobHunter.Application.Tests.Ratings;

/// <summary>
/// F4 T20 done-when 1 and 5: the weekly rating loop posts one "worth opening?" prompt for each of the previous
/// week's top-ten delivered cards, to the Owner's chat, over the half-open <c>[DueAt - 7d, DueAt)</c> window —
/// and does it once per week. The load-bearing properties: a first tick opens the round and prompts every top
/// card; a redelivered tick for the same week opens nothing and sends nothing (idempotence, done-when 5); a
/// week that delivered nothing prompts nothing. Every collaborator is faked, so these are zero-database,
/// zero-network unit tests.
/// </summary>
public sealed class WeeklyRatingHandlerTests
{
    private const long OwnerChat = 4242;
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WeekStart = Now.AddDays(-7);
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000C8");

    private readonly FakeNotifier _notifier = new();
    private readonly FakeRatingRoundLog _roundLog = new();
    private readonly FakeClock _clock = new(Now);
    private readonly IWeeklyTopCardsQuery _topCards = Substitute.For<IWeeklyTopCardsQuery>();

    public WeeklyRatingHandlerTests()
    {
        _topCards
            .TopCardsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Cards(3));
    }

    private static List<WeeklyTopCard> Cards(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new WeeklyTopCard(Guid.Parse($"00000000-0000-0000-0000-{i:D12}"), RunId, i))
            .ToList();

    private WeeklyRatingHandler CreateHandler() =>
        new(_topCards, new FakeWeeklyRatingRenderer(), _roundLog, _notifier, _clock,
            new DeliveryOptions { OwnerChatId = OwnerChat }, NullLogger<WeeklyRatingHandler>.Instance);

    private static WeeklyRatingDue Message() => new(Now);

    [Fact]
    public async Task A_first_tick_prompts_every_top_card_to_the_owner_chat()
    {
        await CreateHandler().Handle(Message(), CancellationToken.None);

        _notifier.SentSubjects.Count.ShouldBe(3);
        _notifier.Sent.ShouldAllBe(s => s.ChatId == OwnerChat);
    }

    [Fact]
    public async Task The_previous_seven_day_window_is_queried()
    {
        await CreateHandler().Handle(Message(), CancellationToken.None);

        await _topCards.Received(1).TopCardsAsync(WeekStart, Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_round_is_opened_for_the_week_under_review()
    {
        await CreateHandler().Handle(Message(), CancellationToken.None);

        _roundLog.Rounds.ShouldHaveSingleItem();
        _roundLog.Rounds.Single().WeekStart.ShouldBe(WeekStart);
        _roundLog.Rounds.Single().ChatId.ShouldBe(OwnerChat);
    }

    [Fact]
    public async Task A_redelivered_tick_for_the_same_week_prompts_nothing()
    {
        await CreateHandler().Handle(Message(), CancellationToken.None);
        var afterFirst = _notifier.SentSubjects.Count;

        await CreateHandler().Handle(Message(), CancellationToken.None);

        _notifier.SentSubjects.Count.ShouldBe(afterFirst);
    }

    [Fact]
    public async Task A_week_that_delivered_nothing_prompts_nothing()
    {
        _topCards
            .TopCardsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await CreateHandler().Handle(Message(), CancellationToken.None);

        _notifier.SentSubjects.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_null_message_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(
            () => CreateHandler().Handle(null!, CancellationToken.None));
    }

    [Theory]
    [InlineData("topCards")]
    [InlineData("renderer")]
    [InlineData("roundLog")]
    [InlineData("notifier")]
    [InlineData("clock")]
    [InlineData("delivery")]
    [InlineData("logger")]
    public void A_null_dependency_is_rejected(string nullDependency)
    {
        IWeeklyTopCardsQuery? topCards = _topCards;
        IWeeklyRatingRenderer? renderer = new FakeWeeklyRatingRenderer();
        IRatingRoundLog? roundLog = _roundLog;
        var notifier = (Domain.Abstractions.INotifier?)_notifier;
        IClock? clock = _clock;
        DeliveryOptions? delivery = new() { OwnerChatId = OwnerChat };
        var logger = NullLogger<WeeklyRatingHandler>.Instance;

        switch (nullDependency)
        {
            case "topCards": topCards = null; break;
            case "renderer": renderer = null; break;
            case "roundLog": roundLog = null; break;
            case "notifier": notifier = null; break;
            case "clock": clock = null; break;
            case "delivery": delivery = null; break;
            case "logger": logger = null!; break;
        }

        Should.Throw<ArgumentNullException>(() =>
            new WeeklyRatingHandler(topCards!, renderer!, roundLog!, notifier!, clock!, delivery!, logger!));
    }
}
