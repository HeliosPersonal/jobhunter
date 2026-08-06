using System.Globalization;
using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/start</c> (contract §Commands): confirms the chat id and that this chat is authorised. It is only
/// ever reached for the Owner — the allowlist gate (<see cref="Auth.OwnerAuthorizer"/>) drops any other chat
/// before a command is routed, so "an unauthorised chat gets no confirmation, only a log entry" holds
/// upstream and this handler has no unauthorised branch. It reads nothing and touches no store.
/// </summary>
internal sealed class StartCommandHandler : ICommandHandler
{
    public Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var chat = request.ChatId.ToString(CultureInfo.InvariantCulture);
        var text = MarkdownV2Escaper.Escape($"This chat ({chat}) is authorised. Send /help for the commands.");

        IReadOnlyList<RenderedMessage> messages = [RenderedMessage.PlainText(text)];
        return Task.FromResult(messages);
    }
}
