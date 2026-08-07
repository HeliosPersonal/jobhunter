using JobHunter.Application.Commands;
using JobHunter.Domain.Commands;
using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/help</c> (contract §Meta, AC-09): the command surface. With no argument it serves the grouped list —
/// the same descriptors the router dispatches on, projected through <see cref="HelpText"/> and framed by
/// <see cref="HelpFormatter"/> — so the help a user reads is exactly what the bot accepts and cannot drift
/// from it. With a command argument it serves that command's detailed usage; an unknown or unrecognised
/// argument falls back to the grouped list, so a mistyped <c>/help x</c> still helps rather than erroring.
/// </summary>
internal sealed class HelpCommandHandler : ICommandHandler
{
    private readonly IReadOnlyList<CommandDescriptor> _commands;

    public HelpCommandHandler(IReadOnlyList<CommandDescriptor> commands) =>
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));

    public Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = request.Arguments is { Length: > 0 } argument
            ? UsageOrGroupedList(argument)
            : GroupedList();

        IReadOnlyList<RenderedMessage> messages = [RenderedMessage.PlainText(text)];
        return Task.FromResult(messages);
    }

    // A named command → its usage; anything else → the grouped list, so /help never dead-ends on a typo.
    private string UsageOrGroupedList(string argument)
    {
        var name = argument.TrimStart('/');
        var descriptor = _commands.FirstOrDefault(
            d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

        return descriptor is null
            ? GroupedList()
            : HelpFormatter.Usage(HelpText.Usage(descriptor));
    }

    private string GroupedList() => HelpFormatter.GroupedList(HelpText.Grouped(_commands));
}
