using System.Globalization;
using System.Text;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Pipeline;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/status</c> (catalogue §Operations · Sensitive · read): the first question R1 asks of the system —
/// the last Run's outcome, its cost against the ceiling, the discovered→matched→delivered·hidden counts, and
/// any degraded sources (AC-06). It resolves the day's Run exactly as delivery and <c>/digest</c> do: the live
/// one while the pipeline works, else the most recent — including a terminal Run, the degraded day that still
/// owes an answer (ADR-F5-0001). The four counts come from the persisted <see cref="JobHunter.Domain.Reporting.Digest"/>,
/// the authoritative artifact assembled before delivery, and the degraded sources from
/// <see cref="IDegradedCoverageQuery"/> as of the injected clock.
///
/// <para>Read-only by construction: no LLM, no write, no CV — the CV crosses exactly one boundary and this is
/// not it. Every dynamic value reaches the reply through the one MarkdownV2 escaper. When no Run exists yet it
/// says so plainly rather than inventing a status; when a live Run exists but its digest is not assembled, it
/// reports the Run's state without fabricating counts.</para>
/// </summary>
internal sealed class StatusCommandHandler(
    IRunRepository runs,
    IDigestRepository digests,
    IDegradedCoverageQuery degradedSources,
    IClock clock,
    ILogger<StatusCommandHandler> logger) : ICommandHandler
{
    private readonly IRunRepository _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly IDigestRepository _digests = digests ?? throw new ArgumentNullException(nameof(digests));
    private readonly IDegradedCoverageQuery _degradedSources =
        degradedSources ?? throw new ArgumentNullException(nameof(degradedSources));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<StatusCommandHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The same run resolution delivery uses: the live Run while the pipeline works, else the most recent —
        // including a terminal Run, which is the degraded day that still owes an answer (ADR-F5-0001).
        var run = await _runs.FindActiveRunAsync(cancellationToken).ConfigureAwait(false)
            ?? await _runs.FindMostRecentRunAsync(cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            _logger.LogDebug("/status requested but no Run exists yet.");
            return [RenderedMessage.PlainText(
                "_" + MarkdownV2Escaper.Escape("No run yet — nothing to report.") + "_")];
        }

        var digest = await _digests.FindByRunAsync(run.Id, cancellationToken).ConfigureAwait(false);
        var degraded = await _degradedSources.DegradedSourcesAsync(_clock.UtcNow, cancellationToken).ConfigureAwait(false);

        var builder = new StringBuilder();

        // The Run's outcome and how long it took — bold state, then the elapsed span when the Run has finished.
        builder.Append('*').Append(MarkdownV2Escaper.Escape(run.State.ToString())).Append('*');
        var elapsed = Elapsed(run);
        if (elapsed is not null)
        {
            builder.Append(" · ").Append(MarkdownV2Escaper.Escape(elapsed));
        }

        builder.Append('\n');

        // Cost against the ceiling — both figures, so the Owner sees the headroom, not just the spend.
        builder.Append(MarkdownV2Escaper.Escape(
            $"Cost: ${Money(run.SpentUsd)} of ${Money(run.CeilingUsd)} ceiling"));

        if (digest is not null)
        {
            // The count line: discovered → matched → delivered · hidden, from the authoritative digest.
            builder.Append('\n');
            builder.Append(MarkdownV2Escaper.Escape(
                $"{run.JobsInScope} discovered → {digest.StrongMatches} matched → "
                + $"{digest.Cards.Count} delivered · {digest.SuppressedCount} hidden"));
        }

        builder.Append('\n');
        builder.Append(DegradedLine(degraded));

        return [RenderedMessage.PlainText(builder.ToString())];
    }

    // The elapsed wall-clock span of a finished Run, rounded to whole minutes; null while it is still running,
    // because a duration for an unfinished Run would be a moving figure the Owner cannot act on.
    private static string? Elapsed(Run run)
    {
        if (run.FinishedAt is not { } finishedAt)
        {
            return null;
        }

        var span = finishedAt - run.StartedAt;
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        var hours = (int)span.TotalHours;
        var minutes = span.Minutes;
        return hours > 0
            ? $"{hours}h {minutes}m"
            : $"{minutes}m";
    }

    // A degraded-source footer: a warning line naming each quarantined provider, or a plain reassurance that
    // every source is healthy — never nothing, so the Owner is always told the coverage state (AC-06).
    private static string DegradedLine(IReadOnlyList<Domain.Sources.DegradedSource> degraded)
    {
        if (degraded.Count == 0)
        {
            return MarkdownV2Escaper.Escape("All sources healthy.");
        }

        var names = string.Join(", ", degraded.Select(d => $"{d.CompanyName} ({d.AtsKind})"));
        return MarkdownV2Escaper.Escape(
            $"⚠️ {degraded.Count} source(s) quarantined: {names}");
    }

    // A dollar figure with two decimals, invariant culture — the digest and ledger both show money this way.
    private static string Money(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);
}
