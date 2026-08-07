namespace JobHunter.Domain.Commands;

/// <summary>
/// One positional argument of a command, as the catalogue and the help both read it (SAD §8). Arguments
/// are forgiving — a missing required argument is never an error but the entry point to the multi-step
/// flow (ADR-F10-0001) — so <see cref="Required"/> is a documentation and help concern, not a hard gate
/// enforced here.
/// </summary>
public sealed record ArgumentSpec
{
    public ArgumentSpec(string name, bool required, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Name = name;
        Required = required;
        Description = description;
    }

    /// <summary>The argument's name as shown in the usage line, e.g. <c>count</c>.</summary>
    public string Name { get; }

    /// <summary>Whether the command needs it; a missing required argument opens the multi-step flow.</summary>
    public bool Required { get; }

    /// <summary>One line describing the argument, shown in per-command help.</summary>
    public string Description { get; }
}
