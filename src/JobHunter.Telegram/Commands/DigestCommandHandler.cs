using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/digest</c> (contract §Commands): re-renders today's digest from stored state so the Owner can re-read
/// the morning's cards on demand. It resolves the day's Run exactly as delivery does — the live one, else the
/// most recent — loads the persisted digest and renders it through the <see cref="IDigestRenderer"/> port.
///
/// <para>Crucially it stops there. Re-rendering is not re-delivering: it takes no <see cref="IDeliveryLog"/>
/// dependency, reads no delivered-keys and writes no log row, so asking for the digest again never re-sends
/// nor double-counts the 07:00 delivery (T11 AC, invariant 8). The dispatcher sends whatever messages this
/// returns. When the day has no Run yet, or a Run exists but its digest has not been assembled, there is
/// nothing to render and the Owner gets one plain "no digest" line rather than an invented one.</para>
/// </summary>
internal sealed class DigestCommandHandler(
    IRunRepository runs,
    IDigestRepository digests,
    IDigestRenderer renderer,
    ILogger<DigestCommandHandler> logger) : ICommandHandler
{
    private readonly IRunRepository _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly IDigestRepository _digests = digests ?? throw new ArgumentNullException(nameof(digests));
    private readonly IDigestRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly ILogger<DigestCommandHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The same run resolution delivery uses: the live Run while the pipeline works, else the most recent —
        // including a terminal Run, which is the degraded day that still owes a digest (ADR-F5-0001).
        var run = await _runs.FindActiveRunAsync(cancellationToken).ConfigureAwait(false)
            ?? await _runs.FindMostRecentRunAsync(cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            _logger.LogDebug("/digest requested but no Run exists for the day; nothing to re-render.");
            return [NothingYet()];
        }

        var digest = await _digests.FindByRunAsync(run.Id, cancellationToken).ConfigureAwait(false);
        if (digest is null)
        {
            _logger.LogDebug("/digest requested for Run {RunId} but no digest is assembled yet.", run.Id);
            return [NothingYet()];
        }

        // Render only — no delivery-log read, no send loop. The dispatcher sends what we return.
        var rendered = await _renderer.RenderAsync(digest, cancellationToken).ConfigureAwait(false);
        return [.. rendered.Select(r => r.Message)];
    }

    private static RenderedMessage NothingYet() =>
        RenderedMessage.PlainText("_" + MarkdownV2Escaper.Escape("No digest is available yet today.") + "_");
}
