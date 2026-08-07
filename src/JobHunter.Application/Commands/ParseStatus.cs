namespace JobHunter.Application.Commands;

/// <summary>
/// The outcome of parsing a command's arguments (SAD §6.1). Parsing is forgiving by design: a missing
/// required argument is <see cref="NeedsInput"/> (the entry to the multi-step flow, not an error), and
/// only a value that cannot be a valid filter is <see cref="Malformed"/>.
/// </summary>
public enum ParseStatus
{
    /// <summary>Arguments parsed; the command can execute.</summary>
    Complete = 1,

    /// <summary>A required argument is missing — enter the multi-step flow and ask (catalogue §Argument parsing).</summary>
    NeedsInput = 2,

    /// <summary>A value did not fit its filter kind — reply with what was wrong and the usage line.</summary>
    Malformed = 3,
}
