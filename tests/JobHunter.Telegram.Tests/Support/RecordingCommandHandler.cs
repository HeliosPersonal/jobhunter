using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Commands;

namespace JobHunter.Telegram.Tests.Support;

/// <summary>
/// A hand-rolled <see cref="ICommandHandler"/> spy: NSubstitute cannot proxy an internal interface, so the
/// command-router suite drives dispatch through this. It records the <see cref="CommandRequest"/> it was
/// handed — so a test can assert the router split the arguments and carried the chat id — and replies with a
/// fixed, caller-supplied message so the router's "return the handler's output" path is observable.
/// </summary>
public sealed class RecordingCommandHandler : ICommandHandler
{
    private readonly IReadOnlyList<RenderedMessage> _reply;

    public RecordingCommandHandler(string reply = "handled")
        : this([RenderedMessage.PlainText(reply)])
    {
    }

    public RecordingCommandHandler(IReadOnlyList<RenderedMessage> reply) => _reply = reply;

    public CommandRequest? Received { get; private set; }

    public int Calls { get; private set; }

    public Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        Received = request;
        Calls++;
        return Task.FromResult(_reply);
    }
}
