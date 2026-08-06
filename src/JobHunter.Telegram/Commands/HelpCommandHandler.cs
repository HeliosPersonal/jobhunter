using JobHunter.Domain.Notifications;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/help</c> (contract §Commands): the command list. It serves the list the <see cref="CommandRouter"/>
/// derives from its registrations, through a supplied accessor, so the help a user reads is exactly the set
/// the router dispatches on and cannot drift from it. The accessor breaks the construction cycle between the
/// router (which needs this handler in its registrations) and this handler (which needs the router's list).
/// </summary>
internal sealed class HelpCommandHandler : ICommandHandler
{
    private readonly Func<string> _helpList;

    public HelpCommandHandler(Func<string> helpList) =>
        _helpList = helpList ?? throw new ArgumentNullException(nameof(helpList));

    public Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<RenderedMessage> messages = [RenderedMessage.PlainText(_helpList())];
        return Task.FromResult(messages);
    }
}
