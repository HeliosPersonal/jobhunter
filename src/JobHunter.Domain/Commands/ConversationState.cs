namespace JobHunter.Domain.Commands;

/// <summary>
/// A chat that is mid-conversation — one of the two genuinely new facts F10 produces (the other is the
/// invocation audit). A multi-step command such as <c>/note</c> with no text stores this while it waits
/// for the Owner's next non-command message (SAD §6.2), so that reply resumes the command rather than
/// being treated as one of its own.
///
/// <para>It is deliberately small: the pending <see cref="Command"/>, the argument it is
/// <see cref="Awaiting"/>, whatever <see cref="Context"/> the command needs to resume (an application id,
/// say) and <see cref="StartedAt"/>. It holds <em>no</em> argument content the Owner has typed — the note
/// body is the resume input, never stored here. The state is bounded by <see cref="Lifetime"/> so a
/// forgotten command never wedges the chat (AC-08); in the store that bound is Redis's native TTL, and
/// <see cref="HasExpired"/> is the same rule evaluated in a test against a clock.</para>
/// </summary>
public sealed record ConversationState
{
    /// <summary>The bound on a pending conversation: five minutes, the Redis TTL (data-model §Conversation state).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public ConversationState(
        string command,
        string awaiting,
        IReadOnlyDictionary<string, string>? context,
        DateTimeOffset startedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(awaiting);

        Command = command;
        Awaiting = awaiting;
        Context = context ?? new Dictionary<string, string>();
        StartedAt = startedAt;
    }

    /// <summary>The pending command's registry name, no slash, e.g. <c>note</c>.</summary>
    public string Command { get; }

    /// <summary>The argument the command is waiting for, e.g. <c>text</c>.</summary>
    public string Awaiting { get; }

    /// <summary>Whatever the command needs to resume — never argument content the Owner typed.</summary>
    public IReadOnlyDictionary<string, string> Context { get; }

    public DateTimeOffset StartedAt { get; }

    /// <summary>Whether the state has reached its lifetime by <paramref name="now"/>; the boundary is inclusive.</summary>
    public bool HasExpired(DateTimeOffset now, TimeSpan lifetime) => now - StartedAt >= lifetime;
}
