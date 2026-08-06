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

        // The command path (T11): the router and its handlers are scoped because a command reads the store,
        // and the singleton processor dispatches through the scope-opening ScopedCommandDispatcher — the same
        // singleton-routes / scope-acts split as the callback path. The /search command reuses the F9 handler;
        // /pipeline is a placeholder until F6 ships. The /help list is derived from the registered set.
        services.AddScoped<Commands.CommandRouter>(BuildCommandRouter);
        services.AddSingleton<Commands.ICommandDispatcher, Commands.ScopedCommandDispatcher>();

        services.AddHostedService<TelegramLongPollService>();

        return services;
    }

    /// <summary>
    /// Assembles the command set for a scope: the F5-owned handlers, the F9 <c>/search</c> adapter, and the
    /// <c>/pipeline</c> placeholder (F6). The <c>/help</c> handler is given a late-bound accessor to the
    /// router's own derived list, so the cycle between the router and the help handler is broken and the help
    /// text is exactly the set the router dispatches on (contract §Commands).
    /// </summary>
    private static Commands.CommandRouter BuildCommandRouter(IServiceProvider provider)
    {
        Commands.CommandRouter? router = null;

        // The order here is the order the commands appear in /help.
        var registrations = new List<Commands.CommandRegistration>
        {
            new("/start", "Confirm this chat is authorised", new Commands.StartCommandHandler()),
            new("/help", "Show this command list", new Commands.HelpCommandHandler(() => router!.HelpList)),
            new("/digest", "Re-read today's digest", new Commands.DigestCommandHandler(
                provider.GetRequiredService<IRunRepository>(),
                provider.GetRequiredService<IDigestRepository>(),
                provider.GetRequiredService<IDigestRenderer>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Commands.DigestCommandHandler>>())),
            new("/saved", "List the roles you saved", new Commands.SavedCommandHandler(
                provider.GetRequiredService<ISavedRolesQuery>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Commands.SavedCommandHandler>>())),
            new("/stats", "This week's engagement", new Commands.StatsCommandHandler(
                provider.GetRequiredService<IWeeklyStatsQuery>(),
                provider.GetRequiredService<IClock>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Commands.StatsCommandHandler>>())),
            new("/pipeline", "Applications by status", new Commands.PlaceholderCommandHandler("Application tracking")),
            new("/search", "Search live roles", new Commands.SearchCommandAdapter(
                provider.GetRequiredService<Search.SearchCommandHandler>())),
        };

        router = new Commands.CommandRouter(
            registrations, provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Commands.CommandRouter>>());
        return router;
    }
}
