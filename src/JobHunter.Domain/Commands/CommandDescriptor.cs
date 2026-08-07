namespace JobHunter.Domain.Commands;

/// <summary>
/// One command's declaration — the whole design of F10 (SAD §5, ADR-F10-0001). The client menu, the
/// <c>/help</c> output, the authorization check and the catalogue-conformance test all derive from a
/// list of these and nothing is hand-maintained, so the surface cannot drift.
///
/// <para>Two guards live here, at construction, not at dispatch: a descriptor is unconstructable without
/// a name, a summary and a <see cref="ContractAnchor"/>, and unconstructable without a declared
/// <see cref="CommandCapability"/> — the default enum value <see cref="CommandCapability.Unspecified"/>
/// is rejected so a forgotten capability fails closed (QG-2). The complementary guard — that a
/// <see cref="ChangesState"/> command carries a <see cref="ConfirmationPrompt"/> — is enforced one layer
/// out, at registry validation (startup), because it is a property of the assembled surface rather than
/// of a single descriptor.</para>
/// </summary>
public sealed record CommandDescriptor
{
    public CommandDescriptor(
        string name,
        string summary,
        IReadOnlyList<ArgumentSpec> args,
        CommandCapability capability,
        CommandGroup group,
        bool changesState,
        string contractAnchor,
        string? confirmationPrompt = null,
        string? example = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractAnchor);

        if (!Enum.IsDefined(capability) || capability == CommandCapability.Unspecified)
        {
            throw new ArgumentException(
                $"Command '{name}' must declare a capability; '{capability}' is not a valid one.",
                nameof(capability));
        }

        if (!Enum.IsDefined(group) || group == CommandGroup.Unspecified)
        {
            throw new ArgumentException(
                $"Command '{name}' must declare a group; '{group}' is not a valid one.",
                nameof(group));
        }

        Name = name;
        Summary = summary;
        Args = args;
        Capability = capability;
        Group = group;
        ChangesState = changesState;
        ContractAnchor = contractAnchor;
        ConfirmationPrompt = confirmationPrompt;
        Example = example;
    }

    /// <summary>The command word, no slash, e.g. <c>pipeline</c>.</summary>
    public string Name { get; }

    /// <summary>One line, shown in the client menu and the grouped help.</summary>
    public string Summary { get; }

    /// <summary>The positional argument specs, in order; may be empty.</summary>
    public IReadOnlyList<ArgumentSpec> Args { get; }

    /// <summary>The command's sensitivity (invariant 9): <see cref="CommandCapability.Standard"/> or Sensitive.</summary>
    public CommandCapability Capability { get; }

    /// <summary>The section this command appears under in the grouped <c>/help</c> and <c>/start</c> lists.</summary>
    public CommandGroup Group { get; }

    /// <summary>Whether the command mutates state; when true a confirmation is required (SAD §6.3).</summary>
    public bool ChangesState { get; }

    /// <summary>The heading in <c>contracts/command-catalogue.md</c> this command maps to, e.g. <c>/pipeline</c>.</summary>
    public string ContractAnchor { get; }

    /// <summary>
    /// The confirmation shown before a state-changing command takes effect, naming the exact effect
    /// (AC-07). Null for a read command; required for a <see cref="ChangesState"/> one, enforced by the
    /// registry at startup rather than here.
    /// </summary>
    public string? ConfirmationPrompt { get; }

    /// <summary>An optional worked example shown in the per-command <c>/help</c> usage line; null when none.</summary>
    public string? Example { get; }
}
