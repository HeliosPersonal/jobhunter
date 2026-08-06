using System.Diagnostics.CodeAnalysis;
using JobHunter.Domain.Abstractions;
using JobHunter.Telegram.Auth;
using JobHunter.Telegram.Callbacks;
using JobHunter.Telegram.Transport;
using Microsoft.Extensions.Options;

namespace JobHunter.Telegram;

/// <summary>
/// The one composition method for the bot host (coding-standards §3): binds and validates
/// <see cref="TelegramOptions"/> at startup — a missing token or an empty allowlist fails the pod at boot,
/// never at first send — and wires the allowlist, the pacer, the <see cref="INotifier"/> and the long-poll
/// hosted service. The bot token is placed only on the named client's base address (<c>…/bot{token}/</c>),
/// so it is never a configuration value read into a log or a span (invariant 12). Excluded from coverage:
/// wiring is verified by the system starting, and the pacing, allowlist and transport behaviours are unit
/// tested directly.
/// </summary>
[ExcludeFromCodeCoverage]
public static class TelegramHostExtensions
{
    public static IServiceCollection AddJobHunterTelegramBot(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection(TelegramOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.BotToken), "Telegram:BotToken is required.")
            .Validate(o => o.AllowedChatIds.Count > 0, "Telegram:AllowedChatIds must list at least one chat.")
            .ValidateOnStart();

        services.AddSingleton<OwnerAuthorizer>();
        services.AddSingleton<ITelegramUpdateProcessor, OwnerGatedUpdateProcessor>();

        // The pacer is a singleton so its slot is shared across every send — one queue, one rate budget.
        services.AddSingleton(sp => new TelegramSendPacer(
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IOptions<TelegramOptions>>().Value.MinSendInterval));

        // The bot token lives only here, on the base address; the request paths are relative and token-free.
        services.AddHttpClient(TelegramNotifier.HttpClientName)
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
                client.BaseAddress = new Uri($"https://api.telegram.org/bot{options.BotToken}/");
            });

        // One TelegramNotifier instance backs both roles: the INotifier the delivery loop sends through and the
        // ICallbackResponder a tap acknowledges through, so acks share the same client and token-free paths.
        services.AddSingleton(sp => new TelegramNotifier(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(TelegramNotifier.HttpClientName),
            sp.GetRequiredService<TelegramSendPacer>(),
            sp.GetRequiredService<IOptions<TelegramOptions>>().Value.MaxSendAttempts,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TelegramNotifier>>()));
        services.AddSingleton<INotifier>(sp => sp.GetRequiredService<TelegramNotifier>());
        services.AddSingleton<ICallbackResponder>(sp => sp.GetRequiredService<TelegramNotifier>());

        // The card-action callback path (T10): the HMAC short-id codec, and the router that opens a DI scope
        // per tap and drives the CallbackHandler. The processor above depends on the router, not the handler,
        // so its routing decision stays a singleton while the action write runs scoped.
        services.AddSingleton<CallbackDataCodec>();
        services.AddSingleton<ICallbackRouter, ScopedCallbackRouter>();

        services.AddHostedService<TelegramLongPollService>();

        return services;
    }
}
