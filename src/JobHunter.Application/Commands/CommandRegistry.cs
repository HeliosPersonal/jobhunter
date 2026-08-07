using JobHunter.Domain.Commands;

namespace JobHunter.Application.Commands;

/// <summary>
/// The single place the command surface is defined (ADR-F10-0001, SAD §5): the menu, <c>/help</c>, the
/// dispatcher's authorization check and the catalogue-conformance test all read this one list. Built once
/// from the descriptors DI provides, and self-validating at construction — which, because it is
/// constructed at startup, is the fail-fast gate QG-2 requires: a malformed surface never serves traffic.
///
/// <para>Validation rejects an empty surface, a duplicate command name (a composition bug caught here
/// rather than a silent last-wins), and — the load-bearing rule — a <see cref="CommandDescriptor.ChangesState"/>
/// command that declares no <see cref="CommandDescriptor.ConfirmationPrompt"/>. A state-changing command
/// without a confirmation path is exactly the one bug in this feature that matters, so it fails the
/// build, naming the offending command.</para>
/// </summary>
public sealed class CommandRegistry
{
    private readonly IReadOnlyDictionary<string, CommandDescriptor> _byName;

    public CommandRegistry(IReadOnlyList<CommandDescriptor> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        if (commands.Count == 0)
        {
            throw new ArgumentException("The command registry must declare at least one command.", nameof(commands));
        }

        var byName = new Dictionary<string, CommandDescriptor>(StringComparer.Ordinal);
        foreach (var command in commands)
        {
            if (!byName.TryAdd(command.Name, command))
            {
                throw new InvalidOperationException(
                    $"More than one command is registered under the name '{command.Name}'.");
            }

            if (command.ChangesState && string.IsNullOrWhiteSpace(command.ConfirmationPrompt))
            {
                throw new InvalidOperationException(
                    $"Command '{command.Name}' changes state and must declare a confirmation prompt (QG-2).");
            }
        }

        Commands = commands;
        _byName = byName;
    }

    /// <summary>The full command surface, in declaration order — readable in one place, which is the point.</summary>
    public IReadOnlyList<CommandDescriptor> Commands { get; }

    /// <summary>The descriptor for <paramref name="name"/> (no slash), or null if no command matches.</summary>
    public CommandDescriptor? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.GetValueOrDefault(name);
    }
}
