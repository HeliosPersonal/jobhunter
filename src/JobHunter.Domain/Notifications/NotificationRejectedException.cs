namespace JobHunter.Domain.Notifications;

/// <summary>
/// A notifier permanently refused a single message — a definitive client error (a Telegram <c>400</c>: a
/// message too long, a chat that blocked the bot, malformed markup) that retrying the same send would not
/// fix (F5 AC-05). It is raised by an <see cref="Abstractions.INotifier"/> implementation and caught by the
/// delivery loop, which logs the one card as failed and delivers the rest rather than abandoning the digest.
///
/// <para>It is deliberately distinct from a <em>transient</em> fault — a dropped connection or an exhausted
/// rate-limit budget — which propagates so the message is retried and delivery resumes from the log. This is
/// the permanent kind: the card cannot be delivered as rendered, so the loop moves on. The exception carries
/// nothing about the Owner or the message body beyond a short reason (invariant 12: no secret, no CV).</para>
/// </summary>
public sealed class NotificationRejectedException : Exception
{
    public NotificationRejectedException(string message)
        : base(message)
    {
    }
}
