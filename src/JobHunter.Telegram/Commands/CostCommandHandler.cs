using System.Globalization;
using System.Text;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/cost [month]</c> (catalogue §Operations · Sensitive · read): the calendar month's spend broken down by
/// pipeline stage and model tier, each line carrying both the estimated and the actual dollars and flagging
/// estimate-vs-actual drift above 20% — the way a stale pricing table surfaces (infrastructure §8). The month
/// comes from the optional <c>YYYY-MM</c> argument, or the current month from the injected clock when none is
/// given; either resolves to the first instant of that month, which the read port sums a half-open window from.
///
/// <para>Read-only by construction: it composes <see cref="IMonthlyCostQuery"/>, the first read side of the
/// append-only cost ledger — no LLM, no write, no CV. A malformed month is a business outcome with a usage line,
/// never an exception, and nothing is read in that case. Drift is computed per line as the fraction the actual
/// sits above the estimate; a zero estimate against real spend is unbounded drift and is flagged rather than
/// divided by. Every dynamic value reaches the reply through the one MarkdownV2 escaper.</para>
/// </summary>
internal sealed class CostCommandHandler(
    IMonthlyCostQuery cost,
    IClock clock,
    ILogger<CostCommandHandler> logger) : ICommandHandler
{
    /// <summary>The drift band a line must stay within; beyond it the estimate no longer tracks the actual.</summary>
    private const decimal DriftThreshold = 0.20m;

    private readonly IMonthlyCostQuery _cost = cost ?? throw new ArgumentNullException(nameof(cost));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<CostCommandHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryResolveMonth(request.Arguments, out var monthStart))
        {
            _logger.LogDebug("/cost rejected an unparseable month argument.");
            return [RenderedMessage.PlainText(
                "_" + MarkdownV2Escaper.Escape("Usage: /cost [YYYY-MM] — e.g. /cost 2026-05.") + "_")];
        }

        var rows = await _cost.BreakdownForMonthAsync(monthStart, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("/cost reported {Lines} stage/tier line(s) for {Month:yyyy-MM}.", rows.Count, monthStart);

        return [RenderedMessage.PlainText(Render(monthStart, rows))];
    }

    // Resolves the month to sum from: the YYYY-MM argument when given, else the current month from the clock.
    // A blank argument is "current month", not an error; a non-empty argument must parse as YYYY-MM exactly.
    private bool TryResolveMonth(string? arguments, out DateTimeOffset monthStart)
    {
        var trimmed = arguments?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            var now = _clock.UtcNow;
            monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
            return true;
        }

        if (DateTimeOffset.TryParseExact(
                trimmed, "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            monthStart = new DateTimeOffset(parsed.Year, parsed.Month, 1, 0, 0, 0, TimeSpan.Zero);
            return true;
        }

        monthStart = default;
        return false;
    }

    // A header naming the month, then one line per (stage, tier) with its estimated and actual dollars — a drift
    // warning appended to any line the actual has pulled more than the threshold above the estimate. A month with
    // no spend says so plainly rather than showing an empty board.
    private static string Render(DateTimeOffset monthStart, IReadOnlyList<CostBreakdownRow> rows)
    {
        var builder = new StringBuilder();
        builder.Append('*')
            .Append(MarkdownV2Escaper.Escape($"Cost — {monthStart.ToString("yyyy-MM", CultureInfo.InvariantCulture)}"))
            .Append('*');

        if (rows.Count == 0)
        {
            builder.Append('\n').Append(MarkdownV2Escaper.Escape("No spend recorded this month."));
            return builder.ToString();
        }

        foreach (var row in rows)
        {
            builder.Append('\n');
            builder.Append(MarkdownV2Escaper.Escape(
                $"{row.Stage} · {row.Tier}: ${Money(row.ActualUsd)} actual vs ${Money(row.EstimatedUsd)} estimated"));

            if (IsDrifting(row))
            {
                builder.Append(MarkdownV2Escaper.Escape($" ⚠️ {DriftPercent(row)}% over"));
            }
        }

        return builder.ToString();
    }

    // A line drifts when the actual sits more than the threshold above the estimate. A zero estimate against real
    // spend is unbounded drift — flagged, never divided by; a zero estimate with no spend is not drift.
    private static bool IsDrifting(CostBreakdownRow row)
    {
        if (row.EstimatedUsd <= 0m)
        {
            return row.ActualUsd > 0m;
        }

        return (row.ActualUsd - row.EstimatedUsd) / row.EstimatedUsd > DriftThreshold;
    }

    // The whole-percent the actual sits above the estimate, for the warning label; "∞" when there was no estimate
    // to drift from, which reads truer than a fabricated number.
    private static string DriftPercent(CostBreakdownRow row)
    {
        if (row.EstimatedUsd <= 0m)
        {
            return "∞";
        }

        var fraction = (row.ActualUsd - row.EstimatedUsd) / row.EstimatedUsd;
        return (fraction * 100m).ToString("0", CultureInfo.InvariantCulture);
    }

    // A dollar figure with two decimals, invariant culture — the digest, ledger and /status all show money so.
    private static string Money(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);
}
