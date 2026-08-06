using System.Diagnostics.CodeAnalysis;
using JobHunter.Domain.Abstractions;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// The scope-per-command glue behind <see cref="ICommandDispatcher"/>: the update processor is a singleton
/// but a command handler reads the store, so each dispatch runs in its own DI scope. It resolves the
/// <see cref="CommandRouter"/> (and, through it, the scoped handlers) from the scope, routes the message and
/// sends each returned message through the singleton <see cref="INotifier"/> — the one boundary to the
/// Owner's chat. Excluded from coverage: it is composition and lifetime wiring; the routing decision is
/// unit-tested on <see cref="CommandRouter"/> and the send loop carries no branching logic of its own.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class ScopedCommandDispatcher(IServiceScopeFactory scopeFactory, INotifier notifier)
    : ICommandDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    private readonly INotifier _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));

    public async Task DispatchAsync(long chatId, string messageText, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var router = scope.ServiceProvider.GetRequiredService<CommandRouter>();

        var messages = await router.RouteAsync(chatId, messageText, cancellationToken).ConfigureAwait(false);
        foreach (var message in messages)
        {
            await _notifier.SendAsync(chatId, message, cancellationToken).ConfigureAwait(false);
        }
    }
}
