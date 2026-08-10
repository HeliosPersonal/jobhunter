using System.Diagnostics.CodeAnalysis;
using JobHunter.Domain.Abstractions;
using JobHunter.Telegram.Callbacks;
using JobHunter.Telegram.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunter.Telegram;

/// <summary>
/// The one composition method for the Telegram <em>outbound send path</em> (Task #88, coding-standards §3):
/// the <see cref="INotifier"/> and the three <see cref="IDigestRenderer"/> / <see cref="IWeeklyRatingRenderer"/>
/// / <see cref="IReminderRenderer"/> implementations the scheduled handlers send through, plus the pacer, the
/// callback codec the renderers sign card-action payloads with, and the token-bearing HTTP client. It lives in
/// the shared adapter so <strong>both</strong> hosts can compose it: the Worker — which runs the Hangfire crons
/// and therefore the delivery / weekly-rating / reminder / regret handlers — and the bot host, which layers its
/// inbound command and callback wiring on top. The bot token is placed only on the named client's base address
/// (<c>…/bot{token}/</c>), so it is never a configuration value read into a log or a span (invariant 12).
/// Excluded from coverage: wiring is verified by the system starting and by the transport composition test; the
/// pacing, rendering and transport behaviours are unit tested directly.
/// </summary>
[ExcludeFromCodeCoverage]
public static class TelegramTransportServiceCollectionExtensions
{
    public static IServiceCollection AddJobHunterTelegramTransport(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // The send path needs the bot token; the allowlist is an inbound concern the bot host validates on top.
        services.AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection(TelegramOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.BotToken), "Telegram:BotToken is required.")
            .ValidateOnStart();

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
            sp.GetRequiredService<ILogger<TelegramNotifier>>()));
        services.AddSingleton<INotifier>(sp => sp.GetRequiredService<TelegramNotifier>());
        services.AddSingleton<ICallbackResponder>(sp => sp.GetRequiredService<TelegramNotifier>());

        // The HMAC short-id codec: shared by the renderers (which sign card-action buttons with it) and the
        // bot host's callback path (which resolves a tap back to its card against it).
        services.AddSingleton<CallbackDataCodec>();

        // The production digest renderer (F5 T12): the IDigestRenderer both the 07:00 delivery loop and /digest
        // depend on. Scoped, because it joins each card's display facts through the scoped ICardDisplayQuery; the
        // codec it signs callback payloads with is the same singleton the callback path resolves against.
        services.AddScoped<IDigestRenderer, Formatting.DigestRenderer>();

        // The weekly rating renderer (F4 T20): the IWeeklyRatingRenderer the precision@10 loop sends its
        // "was this worth opening?" prompts through. Scoped for the same reason as the digest renderer — it
        // joins each top-ten card's display facts through the scoped ICardDisplayQuery — and it signs its rating
        // buttons with the same CallbackDataCodec singleton the callback path resolves them against.
        services.AddScoped<IWeeklyRatingRenderer, Formatting.WeeklyRatingRenderer>();

        // The reminder renderer (F6 T06): the IReminderRenderer the 08:00 reminder sweep nudges through. It
        // reads only the public job facts on the DueReminder and shares the one MarkdownV2 escaper, so a hostile
        // title cannot break the send. Stateless, so a singleton is enough.
        services.AddSingleton<IReminderRenderer, Formatting.ReminderRenderer>();

        return services;
    }
}
