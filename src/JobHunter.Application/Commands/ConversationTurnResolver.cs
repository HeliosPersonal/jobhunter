using JobHunter.Domain.Commands;

namespace JobHunter.Application.Commands;

/// <summary>
/// The pure decision at the head of dispatch (SAD §6.2): given whatever conversation is pending for the
/// chat, the incoming message and the clock instant, what the dispatcher should do about it — before the
/// message is resolved as a command at all. It holds no store and no clock, so the whole AC-08 state
/// machine — resume, supersede, cancel, expire, or nothing-to-cancel — is decided here and unit-tested in
/// isolation.
///
/// <para>Two rules order the branches. Expiry wins over everything: a pending command past its lifetime
/// is reported <see cref="ConversationDisposition.Expired"/> and never swallows the next message, whether
/// that message is free text or another command — the Redis TTL would have removed it, and this is the
/// same rule for a caller that read a still-live copy. Below that, <c>/cancel</c> is always honoured, a
/// new command supersedes a live pending one, and any other message resumes it as input.</para>
/// </summary>
public static class ConversationTurnResolver
{
    public static ConversationTurn Resolve(ConversationState? pending, string message, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(message);

        var isCommand = IsCommand(message);
        var isCancel = isCommand && CommandWord(message) == "cancel";

        if (pending is null)
        {
            return isCancel ? ConversationTurn.NothingToCancel() : ConversationTurn.Proceed();
        }

        if (pending.HasExpired(now, ConversationState.Lifetime))
        {
            return ConversationTurn.For(ConversationDisposition.Expired, pending);
        }

        if (isCancel)
        {
            return ConversationTurn.For(ConversationDisposition.Cancelled, pending);
        }

        return isCommand
            ? ConversationTurn.For(ConversationDisposition.Superseded, pending)
            : ConversationTurn.Resume(pending, message);
    }

    private static bool IsCommand(string message)
    {
        var trimmed = message.TrimStart();
        return trimmed.Length > 0 && trimmed[0] == '/';
    }

    // The command word without its slash or any "@BotName" suffix Telegram appends in a group.
    private static string CommandWord(string message)
    {
        var trimmed = message.Trim();
        var spaceIndex = trimmed.IndexOf(' ', StringComparison.Ordinal);
        var token = spaceIndex < 0 ? trimmed : trimmed[..spaceIndex];
        var word = token[1..];
        var atIndex = word.IndexOf('@', StringComparison.Ordinal);
        return atIndex >= 0 ? word[..atIndex] : word;
    }
}
