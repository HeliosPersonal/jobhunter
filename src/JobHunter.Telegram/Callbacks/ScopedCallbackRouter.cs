using System.Diagnostics.CodeAnalysis;
using JobHunter.Application.Actions;
using JobHunter.Domain.Abstractions;
using JobHunter.Telegram.Transport;
using Microsoft.Extensions.Options;

namespace JobHunter.Telegram.Callbacks;

/// <summary>
/// The scope-per-callback glue behind <see cref="ICallbackRouter"/>: the update processor is a singleton but
/// the card-action path reads and writes the store, so each tap runs in its own DI scope. It resolves the
/// scoped collaborators, builds the <see cref="CallbackHandler"/> with the caller-owned resolution window and
/// hands the callback off. Excluded from coverage — it is composition and lifetime wiring; the routing
/// decision is unit-tested on <see cref="OwnerGatedUpdateProcessor"/> and the handler's behaviour on
/// <see cref="CallbackHandler"/>.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class ScopedCallbackRouter(
    IServiceScopeFactory scopeFactory,
    CallbackDataCodec codec,
    ICallbackResponder responder,
    IClock clock,
    IOptions<TelegramOptions> options,
    Microsoft.Extensions.Logging.ILogger<CallbackHandler> handlerLogger) : ICallbackRouter
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    private readonly CallbackDataCodec _codec = codec ?? throw new ArgumentNullException(nameof(codec));
    private readonly ICallbackResponder _responder = responder ?? throw new ArgumentNullException(nameof(responder));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly TimeSpan _window = (options ?? throw new ArgumentNullException(nameof(options))).Value.CallbackResolutionWindow;
    private readonly Microsoft.Extensions.Logging.ILogger<CallbackHandler> _handlerLogger =
        handlerLogger ?? throw new ArgumentNullException(nameof(handlerLogger));

    public async Task RouteAsync(TelegramCallbackQuery callback, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        var handler = new CallbackHandler(
            provider.GetRequiredService<ICardResolutionQuery>(),
            _codec,
            provider.GetRequiredService<RecordCardActionHandler>(),
            _responder,
            _clock,
            _window,
            _handlerLogger);

        await handler.HandleAsync(callback, cancellationToken).ConfigureAwait(false);
    }
}
