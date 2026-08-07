namespace JobHunter.Domain.Commands;

/// <summary>
/// How one command attempt ended (F10 data-model §command_invocations). Every dispatch records exactly
/// one of these, and the set is closed: the audit and the usage metric ([[PRD]] §7) both read it, and a
/// new value would silently escape both. <see cref="Unspecified"/> is the default enum value on purpose —
/// an invocation that forgets to state how it ended fails closed at construction rather than being logged
/// as a success (mirroring <see cref="CommandCapability.Unspecified"/>).
/// </summary>
public enum CommandOutcome
{
    /// <summary>No outcome recorded. Never valid on a constructed invocation — the guard rejects it.</summary>
    Unspecified = 0,

    /// <summary>The command resolved and its handler completed.</summary>
    Succeeded = 1,

    /// <summary>The command word matched nothing in the registry.</summary>
    Unknown = 2,

    /// <summary>The chat is not the Owner; dropped before resolution, told nothing (AC-10).</summary>
    Unauthorised = 3,

    /// <summary>The arguments could not be parsed to the command's shape.</summary>
    Malformed = 4,

    /// <summary>Refused by the per-chat rate limit (SAD §8).</summary>
    Throttled = 5,

    /// <summary>Abandoned before completing — a cancelled multi-step flow or an unconfirmed sensitive command.</summary>
    Cancelled = 6,

    /// <summary>The handler faulted on an infrastructure error.</summary>
    Failed = 7,
}
