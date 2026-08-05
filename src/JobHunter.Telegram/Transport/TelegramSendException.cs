namespace JobHunter.Telegram.Transport;

/// <summary>
/// A send that Telegram refused or throttled past its retry budget (SAD §7). This is an infrastructure
/// fault, not a business outcome: the delivery handler catches it, leaves the delivery-log row unwritten so
/// the card is retried on the next attempt, and never double-sends (invariant 8). The message never contains
/// the bot token (invariant 12).
/// </summary>
public sealed class TelegramSendException : Exception
{
    public TelegramSendException(string message) : base(message)
    {
    }

    public TelegramSendException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
