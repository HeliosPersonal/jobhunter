using JobHunter.Domain.Commands;

namespace JobHunter.Application.Commands;

/// <summary>
/// One entry in the Telegram client menu: a command word without its slash and the one-line summary shown
/// beside it. Telegram's <c>setMyCommands</c> adds the slash itself, so <see cref="Command"/> is the bare
/// name (SAD §4 S5).
/// </summary>
public sealed record BotMenuEntry(string Command, string Description);

/// <summary>
/// Projects the command surface into the client menu (AC-01, conformance assertion 3). The menu is derived
/// from the same descriptor list the router dispatches on, in catalogue order, so <c>setMyCommands</c>
/// carries exactly the registered commands and their summaries — it cannot drift from the surface, and it is
/// never hand-maintained. <see cref="CommandCapability.Sensitive"/> commands are included deliberately:
/// there is one Owner (invariant 9), and hiding recovery commands would only make recovery harder to find.
/// </summary>
public static class BotMenu
{
    /// <summary>The menu entries for <paramref name="descriptors"/>, in their given order.</summary>
    public static IReadOnlyList<BotMenuEntry> From(IReadOnlyList<CommandDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        return descriptors.Select(d => new BotMenuEntry(d.Name, d.Summary)).ToList();
    }
}
