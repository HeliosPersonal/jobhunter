using JobHunter.Domain.Commands;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The store of pending, single-use confirmations for state-changing commands (SAD §6.3, data-model
/// §Conversation state). Its one implementation is Redis-backed under <c>{env}:jobhunter:confirm:{nonce}</c>
/// with a native 120-second TTL, and — the load-bearing property — <see cref="RedeemAsync"/> reads and
/// deletes in one atomic step, so a token can be redeemed at most once even if the Owner double-taps: the
/// first tap gets the token, the second finds nothing. The TTL <em>is</em> the expiry, so a forgotten
/// confirmation cannot replay and no sweeper can fail.
///
/// <para>Unlike the conversation-state store, a confirmation outage is <em>not</em> swallowed: a
/// state-changing command must fail closed. If the store is unreachable, issuing raises and redemption
/// reports the token as not found — the command is refused, never run unconfirmed.</para>
/// </summary>
public interface IConfirmationStore
{
    /// <summary>Stores <paramref name="token"/> under its nonce with the lifetime TTL, so a later tap can redeem it.</summary>
    Task IssueAsync(ConfirmationToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically reads and burns the token for <paramref name="nonce"/>, returning it, or <c>null</c> when
    /// none exists — already used, expired by TTL, never issued, or the store is unreachable. The delete is
    /// part of the read, so a second concurrent tap of the same nonce cannot both succeed (single use).
    /// </summary>
    Task<ConfirmationToken?> RedeemAsync(string nonce, CancellationToken cancellationToken = default);
}
