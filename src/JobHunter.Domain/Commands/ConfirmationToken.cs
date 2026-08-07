namespace JobHunter.Domain.Commands;

/// <summary>
/// A single-use intent to run a state-changing command (SAD §6.3, AC-07). A <see cref="CommandDescriptor.ChangesState"/>
/// command is never run on the first message: the dispatcher issues one of these, names the exact effect
/// in a keyboard, and only runs the command when the Owner taps back a callback carrying the
/// <see cref="Nonce"/>. That indirection is what a fat thumb cannot supply and a stale tap must not replay.
///
/// <para>The token binds the <see cref="Nonce"/> to the <see cref="ChatId"/>, the <see cref="Command"/>
/// and its <see cref="ArgumentTail"/>, so the tap runs exactly the command that was confirmed and no other.
/// It is <em>single-use</em>: redemption burns it (in the store, by deletion; here, <see cref="Redeemed"/>
/// marks a copy <see cref="Used"/>) so a second tap finds nothing. It is bounded by <see cref="Lifetime"/>
/// so a forgotten confirmation cannot replay; in the store that bound is Redis's native TTL, and
/// <see cref="HasExpired"/> is the same rule evaluated in a test against a clock.</para>
/// </summary>
public sealed record ConfirmationToken
{
    /// <summary>The bound on a pending confirmation: two minutes, the Redis TTL (data-model §Conversation state).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    public ConfirmationToken(
        string nonce,
        long chatId,
        string command,
        string argumentTail,
        DateTimeOffset issuedAt,
        bool used = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(argumentTail);

        Nonce = nonce;
        ChatId = chatId;
        Command = command;
        ArgumentTail = argumentTail;
        IssuedAt = issuedAt;
        Used = used;
    }

    /// <summary>The opaque, unguessable id the confirmation keyboard carries back in its callback.</summary>
    public string Nonce { get; }

    /// <summary>The chat the confirmation was issued to; the tap must come from the same chat.</summary>
    public long ChatId { get; }

    /// <summary>The command the tap runs, registry name, no slash, e.g. <c>run</c>.</summary>
    public string Command { get; }

    /// <summary>The raw argument tail the confirmed command runs with; may be empty, never null.</summary>
    public string ArgumentTail { get; }

    public DateTimeOffset IssuedAt { get; }

    /// <summary>Whether the token has already been redeemed; a second tap of a used token is refused.</summary>
    public bool Used { get; }

    /// <summary>Whether the token has reached its lifetime by <paramref name="now"/>; the boundary is inclusive.</summary>
    public bool HasExpired(DateTimeOffset now, TimeSpan lifetime) => now - IssuedAt >= lifetime;

    /// <summary>A copy marked <see cref="Used"/>; the burn that makes the token single-use.</summary>
    public ConfirmationToken Redeemed() =>
        new(Nonce, ChatId, Command, ArgumentTail, IssuedAt, used: true);
}
