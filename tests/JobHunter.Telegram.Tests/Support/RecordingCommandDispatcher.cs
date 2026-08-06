using System.Collections.Concurrent;
using JobHunter.Telegram.Commands;

namespace JobHunter.Telegram.Tests.Support;

/// <summary>
/// A hand-rolled <see cref="ICommandDispatcher"/> spy — NSubstitute cannot proxy an internal interface — so
/// the update-processor suite can assert that an authorised <c>/</c>-message is dispatched to the command
/// path (and a non-command message and an unauthorised message are not) without opening a real DI scope.
/// </summary>
public sealed class RecordingCommandDispatcher : ICommandDispatcher
{
    private readonly ConcurrentQueue<(long ChatId, string Text)> _dispatched = new();

    public IReadOnlyCollection<(long ChatId, string Text)> Dispatched => _dispatched.ToArray();

    public Task DispatchAsync(long chatId, string messageText, CancellationToken cancellationToken = default)
    {
        _dispatched.Enqueue((chatId, messageText));
        return Task.CompletedTask;
    }
}
