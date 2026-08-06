using System.Globalization;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/stats</c> (contract §Commands, retained never dropped — T11): this week's engagement in one glance —
/// delivered, opened, ignored, saved, applied — with the precision of the week's cards and how that
/// precision moved against the week before. It reads two adjacent, half-open windows through
/// <see cref="IWeeklyStatsQuery"/> — this week <c>[now-7d, now)</c> and the prior week <c>[now-14d, now-7d)</c>
/// — and computes the precision and the trend here, so the arithmetic is unit-tested against the clock rather
/// than a database (invariant: <see cref="IClock"/> everywhere, never <c>DateTime.Now</c>).
///
/// <para>Reading is all it does: no LLM (the command path is deterministic — ADR-F10-0002), no CV, no write.
/// An empty week is answered with a single plain line, so the Owner always gets a reply.</para>
/// </summary>
internal sealed class StatsCommandHandler(
    IWeeklyStatsQuery stats, IClock clock, ILogger<StatsCommandHandler> logger) : ICommandHandler
{
    /// <summary>The reporting window: the trailing seven days, compared against the seven before it.</summary>
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    private readonly IWeeklyStatsQuery _stats = stats ?? throw new ArgumentNullException(nameof(stats));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<StatsCommandHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.UtcNow;
        var thisWeekFrom = now - Week;
        var priorWeekFrom = now - Week - Week;

        var thisWeek = await _stats.EngagementAsync(thisWeekFrom, now, cancellationToken).ConfigureAwait(false);
        var priorWeek = await _stats.EngagementAsync(priorWeekFrom, thisWeekFrom, cancellationToken)
            .ConfigureAwait(false);

        if (thisWeek.Delivered == 0)
        {
            _logger.LogDebug("/stats requested but no cards were delivered this week.");
            return [RenderedMessage.PlainText(
                "_" + MarkdownV2Escaper.Escape("Nothing delivered this week yet.") + "_")];
        }

        return [RenderedMessage.PlainText(Render(thisWeek, priorWeek))];
    }

    private static string Render(WeeklyEngagement week, WeeklyEngagement prior)
    {
        var lines = new List<string>
        {
            "*" + MarkdownV2Escaper.Escape("This week") + "*",
            MarkdownV2Escaper.Escape($"Delivered: {week.Delivered}"),
            MarkdownV2Escaper.Escape($"Opened: {week.Opened}"),
            MarkdownV2Escaper.Escape($"Ignored: {week.Ignored}"),
            MarkdownV2Escaper.Escape($"Saved: {week.Saved}"),
            MarkdownV2Escaper.Escape($"Applied: {week.Applied}"),
            MarkdownV2Escaper.Escape($"Precision: {FormatPercent(week.Precision)}") + " " + Trend(week, prior),
        };

        return string.Join("\n", lines);
    }

    private static string FormatPercent(decimal? fraction) =>
        fraction is { } value
            ? Math.Round(value * 100m, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%"
            : "—";

    // A glyph, not a colour: ▲ up, ▼ down, ▬ flat, and nothing at all when the prior week set no baseline.
    private static string Trend(WeeklyEngagement week, WeeklyEngagement prior)
    {
        if (week.Precision is not { } current || prior.Precision is not { } previous)
        {
            return string.Empty;
        }

        return current > previous ? "▲"
            : current < previous ? "▼"
            : "▬";
    }
}
