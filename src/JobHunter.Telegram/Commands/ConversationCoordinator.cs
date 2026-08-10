using JobHunter.Application.Commands;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// The conversation-aware head of dispatch (T10 S5, SAD §6.2). Before a message is routed as a command, it
/// asks the pure <see cref="ConversationTurnResolver"/> what to do about whatever is pending for the chat and
/// acts on the answer: a free-text reply <em>resumes</em> the pending command through the
/// <see cref="CommandRouter"/> (so <c>/note</c> with no text, then a plain reply, records the note — AC-08);
/// <c>/cancel</c> clears the pending state and confirms, or says there was nothing to cancel; a new command
/// supersedes a stale pending one, and an expired one is dropped — both then route fresh; and a stray
/// non-command message with nothing pending falls through to the router, which answers it as an unknown
/// command, never a conversational reply (AC-09).
///
/// <para>Expiry is decided against <see cref="IClock"/> the same way the Redis TTL decides it in production,
/// so a still-live copy read from a store that hasn't yet evicted it is treated as expired here rather than
/// resuming a command the Owner has long since abandoned. The coordinator clears the state on cancel,
/// supersede and expiry; a resumed command clears its own state as part of completing its step, since only
/// the handler knows whether the step was terminal.</para>
/// </summary>
internal sealed class ConversationCoordinator
{
    private readonly IConversationStateStore _state;
    private readonly CommandRouter _router;
    private readonly IClock _clock;
    private readonly ILogger<ConversationCoordinator> _logger;

    public ConversationCoordinator(
        IConversationStateStore state,
        CommandRouter router,
        IClock clock,
        ILogger<ConversationCoordinator> logger)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<RenderedMessage>> DispatchAsync(
        long chatId, string messageText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageText);

        // Best-effort by contract: a store outage returns null and degrades to a plain dispatch, never faults.
        var pending = await _state.GetAsync(chatId, cancellationToken).ConfigureAwait(false);
        var turn = ConversationTurnResolver.Resolve(pending, messageText, _clock.UtcNow);

        switch (turn.Disposition)
        {
            case ConversationDisposition.Resume:
                _logger.LogDebug("Resuming a pending conversation with the Owner's reply.");
                return await _router
                    .ResumeAsync(chatId, turn.Pending!, turn.Input!, cancellationToken)
                    .ConfigureAwait(false);

            case ConversationDisposition.Cancelled:
                await _state.ClearAsync(chatId, cancellationToken).ConfigureAwait(false);
                return [RenderedMessage.PlainText(MarkdownV2Escaper.Escape("Cancelled."))];

            case ConversationDisposition.NothingToCancel:
                return [RenderedMessage.PlainText(MarkdownV2Escaper.Escape("Nothing to cancel."))];

            case ConversationDisposition.Superseded:
            case ConversationDisposition.Expired:
                // The pending command is abandoned (a newer command, or a lapsed one): clear it and route the
                // message as if nothing had been pending.
                await _state.ClearAsync(chatId, cancellationToken).ConfigureAwait(false);
                return await _router.RouteAsync(chatId, messageText, cancellationToken).ConfigureAwait(false);

            default:
                // Proceed: nothing pending, dispatch the message on its own merits.
                return await _router.RouteAsync(chatId, messageText, cancellationToken).ConfigureAwait(false);
        }
    }
}
