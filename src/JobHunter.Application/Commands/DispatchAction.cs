namespace JobHunter.Application.Commands;

/// <summary>
/// What the dispatcher should do with one resolved command line (SAD §6.1). It is the pure decision the
/// <see cref="CommandDispatchPlanner"/> reaches after resolution and parsing, before any side effect: the
/// Telegram layer turns it into a reply, a confirmation keyboard, a stored conversation state or a handler
/// call. Keeping it a value means the ordering rules — an unknown command is never parsed, a state-changing
/// command never proceeds without confirmation — are unit-testable with no chat and no clock.
/// </summary>
public enum DispatchAction
{
    /// <summary>Resolved, parsed and read-only — run the handler.</summary>
    Proceed = 1,

    /// <summary>No command matched the word; the Telegram layer offers the nearest suggestion (AC-09).</summary>
    Unknown = 2,

    /// <summary>A required argument was absent — open the multi-step flow and ask for it (never an error).</summary>
    NeedsInput = 3,

    /// <summary>A value could not fit its declared shape — reply with the problem and the usage line.</summary>
    Malformed = 4,

    /// <summary>The command changes state — issue a single-use confirmation before any handler runs (AC-07).</summary>
    NeedsConfirmation = 5,
}
