namespace JobHunter.Domain.Notifications;

/// <summary>
/// One message the bot is about to send: MarkdownV2 <see cref="Text"/> and an optional inline keyboard
/// (SAD §5). It is the value the rendering corpus captures against a fake <c>INotifier</c> — the layout is
/// asserted here, before any transport is involved, so 200 layout cases run on every PR with zero network.
/// The text is assumed already escaped by the formatting layer; the notifier is a transport, not an
/// escaper.
/// </summary>
/// <param name="Text">The MarkdownV2 body, already escaped by the formatter (F5 T06).</param>
/// <param name="Keyboard">The inline keyboard rows, empty when the message carries no actions.</param>
public sealed record RenderedMessage(string Text, IReadOnlyList<IReadOnlyList<InlineButton>> Keyboard)
{
    /// <summary>A text-only message — a header or footer carries no buttons.</summary>
    public static RenderedMessage PlainText(string text) => new(text, []);

    /// <summary>True when the message has at least one button, so the send includes a reply markup.</summary>
    public bool HasKeyboard => Keyboard.Count > 0 && Keyboard.Any(row => row.Count > 0);
}

/// <summary>
/// One inline-keyboard button: the visible <see cref="Label"/> and the opaque <see cref="CallbackData"/>
/// Telegram echoes back on a tap. The callback data is a short id, never a job fact (SAD §6.2); resolving
/// it to a card is the bot's job, not the button's.
/// </summary>
/// <param name="Label">The button caption shown to the Owner.</param>
/// <param name="CallbackData">The opaque token echoed back in the callback query.</param>
public sealed record InlineButton(string Label, string CallbackData);
