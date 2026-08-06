using System.Text.Json.Serialization;

namespace JobHunter.Telegram.Transport;

/// <summary>
/// The slice of a Telegram <c>Update</c> the bot reads (SAD §6.2): the update id the long-poll loop
/// acknowledges, and whichever of a message or a callback query carries the originating chat. Only the
/// fields the allowlist and routing need are modelled — the payload Telegram sends is far larger.
/// </summary>
public sealed record TelegramUpdate(
    [property: JsonPropertyName("update_id")] long UpdateId,
    [property: JsonPropertyName("message")] TelegramMessage? Message,
    [property: JsonPropertyName("callback_query")] TelegramCallbackQuery? CallbackQuery)
{
    /// <summary>
    /// The chat id this update originated from, from whichever of a message or a callback query is present,
    /// or <c>null</c> when neither carries one — an update the allowlist treats as not the Owner's.
    /// </summary>
    public long? ChatId => Message?.Chat?.Id ?? CallbackQuery?.Message?.Chat?.Id;
}

/// <summary>
/// A Telegram message, reduced to its id, chat and text. The <c>message_id</c> is what a callback needs to
/// edit the right card's keyboard after a tap — cards are sent as separate messages precisely so an edit
/// touches only the tapped one (contract §Card).
/// </summary>
public sealed record TelegramMessage(
    [property: JsonPropertyName("chat")] TelegramChat? Chat,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("message_id")] long MessageId = 0);

/// <summary>A callback query from an inline-keyboard tap, reduced to its data and the message it hangs off.</summary>
public sealed record TelegramCallbackQuery(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("data")] string? Data,
    [property: JsonPropertyName("message")] TelegramMessage? Message);

/// <summary>A Telegram chat, reduced to the id the allowlist checks.</summary>
public sealed record TelegramChat(
    [property: JsonPropertyName("id")] long Id);
