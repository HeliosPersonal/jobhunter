using System.Globalization;
using System.Text;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Sources;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/sources</c> (catalogue §Operations · Sensitive · read, R4): the source-health board the Owner reads
/// without a terminal. It composes two reads — each ATS provider's attempts and successes over the trailing 24
/// hours (<see cref="ISourceHealthQuery"/>), and the sources currently quarantined as of the clock
/// (<see cref="IDegradedCoverageQuery"/>) — into one message per section. Each quarantined source carries a
/// release button, R4's main action: a callback whose payload names the source to release.
///
/// <para>Read-only in this task by construction: the release <em>tap</em> is a callback routed by the dispatch
/// rewire in T10 — the same singleton-routes / scope-acts split the card-action taps use — which then calls
/// <see cref="JobHunter.Application.Search.SourceQuarantineService"/>. This handler renders the button; it does
/// not write. Every dynamic value reaches the reply through the one MarkdownV2 escaper.</para>
/// </summary>
internal sealed class SourcesCommandHandler(
    ISourceHealthQuery health,
    IDegradedCoverageQuery degradedSources,
    IClock clock,
    ILogger<SourcesCommandHandler> logger) : ICommandHandler
{
    /// <summary>The trailing window the health board reports over (catalogue §Operations: "the last 24 hours").</summary>
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>
    /// The callback payload prefix a release button carries. The T10 dispatch rewire routes a tap whose data starts
    /// with this to the quarantine-release write; the suffix is the source id to release.
    /// </summary>
    public const string ReleasePrefix = "release:";

    private readonly ISourceHealthQuery _health = health ?? throw new ArgumentNullException(nameof(health));
    private readonly IDegradedCoverageQuery _degradedSources =
        degradedSources ?? throw new ArgumentNullException(nameof(degradedSources));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<SourcesCommandHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.UtcNow;
        var health = await _health.HealthSinceAsync(now - Window, cancellationToken).ConfigureAwait(false);
        var quarantined = await _degradedSources.DegradedSourcesAsync(now, cancellationToken).ConfigureAwait(false);

        var messages = new List<RenderedMessage> { HealthMessage(health) };
        messages.AddRange(QuarantineMessages(quarantined));

        _logger.LogDebug(
            "/sources reported {Providers} provider(s) and {Quarantined} quarantined source(s).",
            health.Count, quarantined.Count);

        return messages;
    }

    // One header plus a line per provider — attempts and successes over the window — or a plain "no attempts" line
    // when nothing was fetched, so the board is never a silent blank.
    private static RenderedMessage HealthMessage(IReadOnlyList<SourceHealth> health)
    {
        if (health.Count == 0)
        {
            return RenderedMessage.PlainText(
                "_" + MarkdownV2Escaper.Escape("No fetch attempts in the last 24 hours.") + "_");
        }

        var builder = new StringBuilder();
        builder.Append('*').Append(MarkdownV2Escaper.Escape("Source health — last 24h")).Append('*');
        foreach (var provider in health)
        {
            builder.Append('\n');
            builder.Append(MarkdownV2Escaper.Escape(
                $"{provider.AtsKind}: {provider.Successes}/{provider.Attempts} succeeded"));
        }

        return RenderedMessage.PlainText(builder.ToString());
    }

    // The quarantine section: a plain reassurance when nothing is quarantined, else one message per source naming
    // it and its release time, carrying the release button the T10 rewire routes.
    private static IEnumerable<RenderedMessage> QuarantineMessages(IReadOnlyList<DegradedSource> quarantined)
    {
        if (quarantined.Count == 0)
        {
            yield return RenderedMessage.PlainText(
                "_" + MarkdownV2Escaper.Escape("Nothing is quarantined.") + "_");
            yield break;
        }

        foreach (var source in quarantined)
        {
            var text = MarkdownV2Escaper.Escape(
                $"⚠️ {source.CompanyName} ({source.AtsKind}) quarantined until "
                + $"{source.QuarantinedUntil.ToString("u", CultureInfo.InvariantCulture)} "
                + $"after {source.ConsecutiveFailures} consecutive failures.");

            var button = new InlineButton(
                $"Release {source.CompanyName}", ReleasePrefix + source.SourceId.ToString("D", CultureInfo.InvariantCulture));

            yield return new RenderedMessage(text, [[button]]);
        }
    }
}
