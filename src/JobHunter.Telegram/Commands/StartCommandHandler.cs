using System.Globalization;
using JobHunter.Application.Commands;
using JobHunter.Domain.Commands;
using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/start</c> (contract §Meta): confirms the chat id and that this chat is authorised, then appends the
/// grouped command list — the same descriptors <c>/help</c> and the router dispatch on — so a first-time
/// Owner sees the whole surface at once and it cannot drift from what the bot accepts. It is only ever
/// reached for the Owner: the allowlist gate (<see cref="Auth.OwnerAuthorizer"/>) drops any other chat before
/// a command is routed, so "an unauthorised chat gets no confirmation, only a log entry" holds upstream and
/// this handler has no unauthorised branch. It reads nothing and touches no store.
/// </summary>
internal sealed class StartCommandHandler : ICommandHandler
{
    private readonly IReadOnlyList<CommandDescriptor> _commands;

    public StartCommandHandler(IReadOnlyList<CommandDescriptor> commands) =>
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));

    public Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var chat = request.ChatId.ToString(CultureInfo.InvariantCulture);
        var greeting = MarkdownV2Escaper.Escape($"This chat ({chat}) is authorised. Here are the commands:");
        var list = HelpFormatter.GroupedList(HelpText.Grouped(_commands));

        IReadOnlyList<RenderedMessage> messages = [RenderedMessage.PlainText(greeting + "\n\n" + list)];
        return Task.FromResult(messages);
    }
}
