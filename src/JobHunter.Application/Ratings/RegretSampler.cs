using System.Globalization;
using JobHunter.Application.Common;
using JobHunter.Application.Ranking;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using Microsoft.Extensions.Logging;
using DeliveryOptions = JobHunter.Application.Delivery.DeliveryOptions;

namespace JobHunter.Application.Ratings;

/// <summary>
/// The regret sampler (F4 T21, ADR-F4-0003): the falsification control for the pre-match filter. Each week it
/// samples the latest Run's pre-match-excluded jobs (a suppressed score with no match), matches them at the
/// <em>cheap</em> tier, and asks the one question the <c>jobhunter.matching.prefiltered</c> counter cannot —
/// not "how much does each rule remove?" but "did a rule remove something the Owner would have wanted?". Any
/// sampled job whose would-be score reaches the presentation threshold is a regret, recorded to
/// <c>jobhunter.matching.regret</c> and alerted to the Owner, because a wrong factual rule silently removing
/// wanted work is the one real risk the ADR names.
///
/// <para>The sample runs once per week, gated by <see cref="IRegretSampleLog"/> exactly as the rating loop is
/// gated by <see cref="IRatingRoundLog"/>: a redelivered or re-scheduled tick opens nothing, so it queries
/// nothing, spends nothing at the cheap tier and raises no duplicate alert (done-when 1). A measured zero is
/// still recorded, so the gauge falls to flat rather than going blank when a good week finds no regret. It
/// reads only public job content and the model's scores — the CV crosses exactly one boundary, and it is not
/// this one.</para>
/// </summary>
public sealed class RegretSampler(
    IFilterExcludedSampleQuery sample,
    IRegretMatcher matcher,
    IRegretSampleLog sampleLog,
    INotifier notifier,
    DeliveryOptions delivery,
    ILogger<RegretSampler> logger)
{
    /// <summary>The sample size the ADR fixes (done-when 1): twenty filtered-out jobs a week.</summary>
    public const int SampleSize = 20;

    private readonly IFilterExcludedSampleQuery _sample = sample ?? throw new ArgumentNullException(nameof(sample));
    private readonly IRegretMatcher _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
    private readonly IRegretSampleLog _sampleLog = sampleLog ?? throw new ArgumentNullException(nameof(sampleLog));
    private readonly INotifier _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
    private readonly DeliveryOptions _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
    private readonly ILogger<RegretSampler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>One week — the half-open window this tick reviews is <c>[DueAt - 7d, DueAt)</c>.</summary>
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    /// <summary>
    /// The <see cref="RegretSampleDue"/> handler: resolves the previous seven-day week from the tick's stamped
    /// instant — exactly as the rating loop does, so both weekly ticks agree on which week they review — and
    /// samples it. Kept a one-liner over <see cref="SampleAsync"/> so the sampler's logic stays unit-tested
    /// without Hangfire or the bus.
    /// </summary>
    public Task Handle(RegretSampleDue message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return SampleAsync(message.DueAt - Week, cancellationToken);
    }

    /// <summary>
    /// Samples and scores the week beginning <paramref name="weekStart"/>, records the regret count, and alerts
    /// the Owner on any regret — once per week. A week already sampled returns immediately, before any query or
    /// spend.
    /// </summary>
    public async Task SampleAsync(DateTimeOffset weekStart, CancellationToken cancellationToken = default)
    {
        // Open the week first, and only once (done-when 1): a redelivered tick short-circuits here, so the
        // excluded-sample query is never run and the cheap tier is never called a second time for the same week.
        var opened = await _sampleLog.TryOpenAsync(weekStart, weekStart, cancellationToken).ConfigureAwait(false);
        if (!opened)
        {
            _logger.LogInformation(
                "Regret sample for the week of {WeekStart:o} was already taken; nothing to sample.", weekStart);
            return;
        }

        var jobs = await _sample.SampleAsync(SampleSize, cancellationToken).ConfigureAwait(false);
        if (jobs.Count == 0)
        {
            // Nothing was excluded this week: regret is zero by definition, recorded so the gauge stays flat.
            Telemetry.MatchingRegret.Record(0);
            _logger.LogInformation("The latest Run excluded no jobs; regret for the week of {WeekStart:o} is zero.", weekStart);
            return;
        }

        var matched = await _matcher.MatchAsync(jobs, cancellationToken).ConfigureAwait(false);
        var regrets = matched
            .Where(m => m.WouldBeScore >= SuppressionEvaluator.PresentationThreshold)
            .OrderByDescending(m => m.WouldBeScore)
            .ToList();

        Telemetry.MatchingRegret.Record(regrets.Count);

        if (regrets.Count == 0)
        {
            _logger.LogInformation(
                "Regret sample for the week of {WeekStart:o}: {Sampled} excluded jobs matched, none above threshold.",
                weekStart, jobs.Count);
            return;
        }

        await AlertAsync(weekStart, jobs.Count, regrets, cancellationToken).ConfigureAwait(false);
    }

    private async Task AlertAsync(
        DateTimeOffset weekStart, int sampled, List<RegretMatch> regrets, CancellationToken cancellationToken)
    {
        // A wrong rule removed work the Owner would have wanted: name the count and the offending jobs so the
        // Owner (and a dashboard) sees the falsification, never a silent filter (invariant 11).
        var lines = regrets.Select(r => string.Create(
            CultureInfo.InvariantCulture, $"• {r.JobId} would have scored {r.WouldBeScore:0.#}"));
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"Pre-match filter regret for the week of {weekStart:yyyy-MM-dd}: {regrets.Count} of {sampled} sampled excluded jobs would have scored above the presentation threshold.\n")
            + string.Join("\n", lines);

        await _notifier
            .SendAsync(_delivery.OwnerChatId, RenderedMessage.PlainText(text), cancellationToken)
            .ConfigureAwait(false);

        _logger.LogWarning(
            "Regret sample for the week of {WeekStart:o}: {Regrets} of {Sampled} excluded jobs would have scored above threshold.",
            weekStart, regrets.Count, sampled);
    }
}
