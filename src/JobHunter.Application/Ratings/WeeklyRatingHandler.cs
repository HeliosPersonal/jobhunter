using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using DeliveryOptions = JobHunter.Application.Delivery.DeliveryOptions;

namespace JobHunter.Application.Ratings;

/// <summary>
/// The weekly rating loop (F4 T20, D5). On the <see cref="WeeklyRatingDue"/> tick it opens the week's rating
/// round — once, gated by <see cref="IRatingRoundLog"/> — then loads the previous seven-day window's top-ten
/// delivered cards and sends the Owner one "was this worth opening?" prompt per card. It is the empirical
/// counterpart to the golden set: the golden set proves the ranking is stable, this measures whether it is
/// good against the Owner's real judgement.
///
/// <para>Idempotence is the point (done-when 5): <see cref="IRatingRoundLog.TryOpenAsync"/> returns
/// <c>false</c> for a week already prompted, so a redelivered or re-scheduled tick opens nothing and sends
/// nothing — the ratings, and therefore <c>precision@10</c>, are never double-counted. The window is the
/// half-open <c>[DueAt - 7d, DueAt)</c>, so two adjacent weeks never both claim a boundary delivery. It reads
/// only public card identity and sends public prompt text — the CV crosses exactly one boundary, and it is not
/// this one.</para>
/// </summary>
public sealed class WeeklyRatingHandler(
    IWeeklyTopCardsQuery topCards,
    IWeeklyRatingRenderer renderer,
    IRatingRoundLog roundLog,
    INotifier notifier,
    IClock clock,
    DeliveryOptions delivery,
    ILogger<WeeklyRatingHandler> logger)
{
    private readonly IWeeklyTopCardsQuery _topCards = topCards ?? throw new ArgumentNullException(nameof(topCards));
    private readonly IWeeklyRatingRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly IRatingRoundLog _roundLog = roundLog ?? throw new ArgumentNullException(nameof(roundLog));
    private readonly INotifier _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly DeliveryOptions _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
    private readonly ILogger<WeeklyRatingHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>One week — the half-open window this tick reviews is <c>[DueAt - 7d, DueAt)</c>.</summary>
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    public async Task Handle(WeeklyRatingDue message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var weekStart = message.DueAt - Week;
        var chatId = _delivery.OwnerChatId;

        // Open the week's round first, and only once (done-when 5): a redelivered or re-scheduled tick for a
        // week already prompted opens nothing here and returns before sending, so no rating is double-counted.
        var opened = await _roundLog
            .TryOpenAsync(weekStart, chatId, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);
        if (!opened)
        {
            _logger.LogInformation(
                "Weekly rating round for the week of {WeekStart:o} was already opened; nothing to prompt.",
                weekStart);
            return;
        }

        // The previous seven days' top-ten delivered cards — the denominator the Owner is asked to rate.
        var cards = await _topCards
            .TopCardsAsync(weekStart, message.DueAt, cancellationToken)
            .ConfigureAwait(false);

        var prompted = 0;
        foreach (var card in cards)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A card whose job has gone since delivery renders to null: it still counts in the delivered top-ten
            // denominator, but there is nothing to show, so it is skipped rather than sent as a fabricated blank.
            var rendered = await _renderer.RenderAsync(card, cancellationToken).ConfigureAwait(false);
            if (rendered is null)
            {
                continue;
            }

            await _notifier.SendAsync(chatId, rendered, cancellationToken).ConfigureAwait(false);
            prompted++;
        }

        _logger.LogInformation(
            "Weekly rating loop for the week of {WeekStart:o} prompted the Owner on {Prompted} card(s).",
            weekStart, prompted);
    }
}
