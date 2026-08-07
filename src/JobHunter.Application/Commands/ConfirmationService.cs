using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Commands;

namespace JobHunter.Application.Commands;

/// <summary>
/// The confirmation gate for state-changing commands (SAD §6.3, AC-07). A
/// <see cref="CommandDescriptor.ChangesState"/> command is never run on the first message: the dispatcher
/// <see cref="IssueAsync">issues</see> a token, shows a keyboard naming the exact effect, and only runs the
/// command when a tap comes back and <see cref="RedeemAsync">redeems</see> the token.
///
/// <para>The service owns the decision, not the store. Redemption asks the store to atomically read-and-burn
/// the nonce — that atomicity is what makes a double-tap safe — then interprets the result against the chat
/// it was issued to and the clock: a token the store no longer holds is <see cref="ConfirmationOutcome.Expired"/>
/// (its TTL swept it, or it never existed); a token already marked used is
/// <see cref="ConfirmationOutcome.AlreadyUsed"/>; a token issued to a different chat is
/// <see cref="ConfirmationOutcome.Mismatch"/>; a token past its <see cref="ConfirmationToken.Lifetime"/> even
/// if Redis has not yet swept it is <see cref="ConfirmationOutcome.Expired"/>; only an unused, in-lifetime,
/// same-chat token is <see cref="ConfirmationOutcome.Confirmed"/>. There is no path that returns a runnable
/// command for anything else.</para>
/// </summary>
public sealed class ConfirmationService(IConfirmationStore store, IClock clock, IIdGenerator ids)
{
    private readonly IConfirmationStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));

    /// <summary>
    /// Issues a single-use token binding a fresh nonce to <paramref name="chatId"/>, <paramref name="command"/>
    /// and <paramref name="argumentTail"/>, stores it under its lifetime TTL, and returns it so the caller can
    /// render the confirmation keyboard. The nonce is short enough to fit Telegram's 64-byte callback cap.
    /// </summary>
    public async Task<ConfirmationToken> IssueAsync(
        long chatId, string command, string argumentTail, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(argumentTail);

        var token = new ConfirmationToken(NewNonce(), chatId, command, argumentTail, _clock.UtcNow);
        await _store.IssueAsync(token, cancellationToken).ConfigureAwait(false);
        return token;
    }

    /// <summary>
    /// Redeems the tap carrying <paramref name="nonce"/> from <paramref name="chatId"/>: burns the token in the
    /// store and decides whether it confirms, was already used, expired, or came from the wrong chat.
    /// </summary>
    public async Task<ConfirmationResult> RedeemAsync(
        string nonce, long chatId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);

        var token = await _store.RedeemAsync(nonce, cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return ConfirmationResult.Refused(ConfirmationOutcome.Expired);
        }

        if (token.Used)
        {
            return ConfirmationResult.Refused(ConfirmationOutcome.AlreadyUsed);
        }

        if (token.ChatId != chatId)
        {
            return ConfirmationResult.Refused(ConfirmationOutcome.Mismatch);
        }

        if (token.HasExpired(_clock.UtcNow, ConfirmationToken.Lifetime))
        {
            return ConfirmationResult.Refused(ConfirmationOutcome.Expired);
        }

        return ConfirmationResult.Confirmed(token.Command, token.ArgumentTail);
    }

    // A URL- and callback_data-safe nonce from a fresh id: 16 bytes -> 22 base64url characters, well under
    // the 64-byte payload cap and unguessable, so a confirmation cannot be forged or replayed by another id.
    private string NewNonce() =>
        Convert.ToBase64String(_ids.NewId().ToByteArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
