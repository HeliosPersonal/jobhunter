using System.Globalization;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Commands;
using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/redeliver</c> (catalogue §Operations · Sensitive · ✎): re-delivers today's digest, safe by construction —
/// the delivery log means an already-sent card is never sent again (ADR-F5-0002, invariant 8). The whole value of
/// the command is honesty about that: the confirmation states how <em>many cards would actually go out</em>,
/// usually zero. It resolves the day's Run as delivery does (the live one, else the most recent), renders the
/// stored digest through the <see cref="IDigestRenderer"/> port, and set-differences the rendered card keys
/// against the delivery log for the requesting chat — the same idempotence key the delivery loop skips on.
///
/// <para>State-changing, so it previews and asks rather than acting: it stores a short-lived per-chat
/// <see cref="ConversationState"/> awaiting confirmation and returns the count with a confirm prompt. It writes
/// nothing and re-sends nothing here. The <em>resume</em> half completes the command: the
/// <see cref="ConversationCoordinator"/> resolves the Owner's <c>confirm</c> reply to a resume and hands it to
/// <see cref="ResumeAsync"/>, which enqueues the delivery trigger through
/// <see cref="IOperationScheduler.EnqueueDigestDelivery"/>. The Telegram host runs no Wolverine bus by design, so
/// — exactly as the Api's operational endpoints do — it hands the work to Hangfire storage, whose server-side
/// Worker runs the trigger and publishes <c>DigestDeliveryDue</c>; the delivery log keeps it idempotent per card
/// (invariant 8). A confirmation is always terminal and clears the state; anything else leaves it pending for
/// another reply. No LLM, no CV. Every value reaches the reply through the one MarkdownV2 escaper.</para>
/// </summary>
internal sealed class RedeliverCommandHandler(
    IRunRepository runs,
    IDigestRepository digests,
    IDigestRenderer renderer,
    IDeliveryLog deliveryLog,
    IConversationStateStore state,
    IOperationScheduler scheduler,
    IClock clock,
    ILogger<RedeliverCommandHandler> logger) : IResumableCommandHandler
{
    /// <summary>The registry name a pending state carries, so the resume step knows which command to resume.</summary>
    private const string CommandName = "redeliver";

    /// <summary>The step the flow waits for — the Owner's confirmation of the previewed redelivery.</summary>
    private const string AwaitingConfirm = "confirm";

    /// <summary>The Run whose digest the confirmed redelivery will re-send, carried as a structured value.</summary>
    private const string RunIdKey = "runId";

    /// <summary>The reply that re-runs delivery; anything else leaves the state pending (SAD §6.2).</summary>
    private const string ConfirmWord = "confirm";

    private readonly IRunRepository _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly IDigestRepository _digests = digests ?? throw new ArgumentNullException(nameof(digests));
    private readonly IDigestRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly IDeliveryLog _deliveryLog = deliveryLog ?? throw new ArgumentNullException(nameof(deliveryLog));
    private readonly IConversationStateStore _state = state ?? throw new ArgumentNullException(nameof(state));
    private readonly IOperationScheduler _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<RedeliverCommandHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The same run resolution delivery uses: the live Run while the pipeline works, else the most recent —
        // including a terminal Run, the degraded day whose digest the Owner may still want re-sent (ADR-F5-0001).
        var run = await _runs.FindActiveRunAsync(cancellationToken).ConfigureAwait(false)
            ?? await _runs.FindMostRecentRunAsync(cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            _logger.LogDebug("/redeliver requested but no Run exists for the day; nothing to redeliver.");
            return [Nothing("No run yet — nothing to redeliver.")];
        }

        var digest = await _digests.FindByRunAsync(run.Id, cancellationToken).ConfigureAwait(false);
        if (digest is null)
        {
            _logger.LogDebug("/redeliver requested for Run {RunId} but no digest is assembled yet.", run.Id);
            return [Nothing("No digest is assembled yet today — nothing to redeliver.")];
        }

        // The would-be-sent count is exactly what the delivery loop would send: the rendered card keys the
        // delivery log has not already recorded for this chat. Usually zero, which is the whole point (AC).
        var rendered = await _renderer.RenderAsync(digest, cancellationToken).ConfigureAwait(false);
        var alreadyDelivered = await _deliveryLog.DeliveredKeysAsync(run.Id, request.ChatId, cancellationToken)
            .ConfigureAwait(false);
        var delivered = new HashSet<string>(alreadyDelivered, StringComparer.Ordinal);
        var wouldSend = rendered.Count(r => !delivered.Contains(r.Key.Value));

        // Preview and ask: store the pending confirm state, then state the count. Nothing is re-sent here — the
        // confirm reply enqueues the redelivery trigger, the same convention /floor's confirm follows.
        var pending = new ConversationState(
            CommandName,
            AwaitingConfirm,
            new Dictionary<string, string> { [RunIdKey] = run.Id.ToString("D", CultureInfo.InvariantCulture) },
            _clock.UtcNow);
        await _state.SetAsync(request.ChatId, pending, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "/redeliver previewed Run {RunId}: {WouldSend} of {Total} card(s) would actually be sent.",
            run.Id, wouldSend, rendered.Count);

        var cardWord = wouldSend == 1 ? "card" : "cards";
        return [RenderedMessage.PlainText(MarkdownV2Escaper.Escape(
            $"Redelivering today's digest would send {wouldSend} {cardWord} — the rest were already delivered. "
            + "Reply confirm to redeliver, or /cancel to stop."))];
    }

    public async Task<IReadOnlyList<RenderedMessage>> ResumeAsync(
        CommandResumeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Anything but a confirmation is not terminal: neither enqueue nor clear, so the Owner can still confirm
        // or /cancel. Re-prompt with the same wording rather than treating the reply as its own command (AC-08).
        if (!string.Equals(request.Input.Trim(), ConfirmWord, StringComparison.OrdinalIgnoreCase))
        {
            return [RenderedMessage.PlainText(MarkdownV2Escaper.Escape(
                "Reply confirm to redeliver, or /cancel to stop."))];
        }

        // A confirmation is terminal — clear the pending state first so nothing wedges the chat on a later reply.
        await _state.ClearAsync(request.ChatId, cancellationToken).ConfigureAwait(false);

        // Re-run delivery the only way a bus-less host can: enqueue the same delivery trigger the 07:00 slot fires,
        // which the Worker's server runs and which publishes DigestDeliveryDue. The delivery log keeps it idempotent
        // per card (invariant 8), so an already-sent card is never re-sent — usually nothing goes out at all.
        var operationId = _scheduler.EnqueueDigestDelivery();
        _logger.LogInformation("/redeliver enqueued a re-delivery as operation {OperationId}.", operationId);

        return [RenderedMessage.PlainText(MarkdownV2Escaper.Escape(
            "Redelivering today's digest now — only cards not already delivered will be sent."))];
    }

    // A plain italic line when there is nothing to redeliver — never a blank, so the Owner is always told why.
    private static RenderedMessage Nothing(string message) =>
        RenderedMessage.PlainText("_" + MarkdownV2Escaper.Escape(message) + "_");
}
