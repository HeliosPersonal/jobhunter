namespace JobHunter.Application.Delivery;

/// <summary>
/// Tunables for digest delivery (F5 SAD §6.1). Bound and validated at startup (coding-standards §options).
/// The one value that matters is <see cref="OwnerChatId"/> — the single chat the daily digest is sent to
/// (invariant 9: one Owner). It is the <c>chat_id</c> half of the delivery-log key <c>(run_id, chat_id,
/// card_key)</c>, so the same digest delivered to the same chat is idempotent by construction. It is kept
/// distinct from the bot's inbound allowlist (<c>Telegram:AllowedChatIds</c>): the allowlist decides which
/// chats may talk to the bot; this decides where the morning digest lands.
/// </summary>
public sealed class DeliveryOptions
{
    public const string SectionName = "Delivery";

    /// <summary>The Owner's Telegram chat id the digest is delivered to. Startup-validated to be non-zero.</summary>
    public long OwnerChatId { get; init; }
}
