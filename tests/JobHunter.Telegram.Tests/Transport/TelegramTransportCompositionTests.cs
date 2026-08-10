using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using JobHunter.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Transport;

/// <summary>
/// The regression guard for Task #88: the outbound send path must compose on its own, without the bot host.
/// The scheduled handlers — <c>DeliveryHandler</c>, <c>WeeklyRatingHandler</c>, <c>ReminderSweepHandler</c>
/// and the F4 <c>RegretSampler</c> — run in the Worker under Hangfire, so the Worker (which never composes the
/// bot's inbound command and callback wiring) must still be able to resolve the <see cref="INotifier"/> and the
/// three renderers those handlers depend on. <see cref="TelegramTransportServiceCollectionExtensions.AddJobHunterTelegramTransport"/>
/// is the one call that supplies them; these tests pin that its send path resolves given only the external
/// ports a host provides (an <see cref="IClock"/> and an <see cref="ICardDisplayQuery"/>), so a future change
/// that quietly re-entangles the send path with the bot host fails here rather than at 07:00 in production.
/// </summary>
public sealed class TelegramTransportCompositionTests
{
    private static ServiceProvider BuildSendPath()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telegram:BotToken"] = "test-token",
                ["Telegram:AllowedChatIds:0"] = "12345",
            })
            .Build();

        var services = new ServiceCollection();

        // The external ports a real host supplies: the clock (ServiceDefaults/Infrastructure) and the card
        // display-facts read model (Infrastructure/Dapper). The transport registration owns everything else.
        services.AddSingleton<IClock>(new FakeClock());
        services.AddSingleton<ICardDisplayQuery>(new EmptyCardDisplayQuery());

        services.AddJobHunterTelegramTransport(configuration);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void The_send_path_resolves_the_notifier_without_the_bot_host()
    {
        using var provider = BuildSendPath();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<INotifier>().ShouldNotBeNull();
    }

    [Fact]
    public void The_send_path_resolves_every_renderer_the_scheduled_handlers_depend_on()
    {
        using var provider = BuildSendPath();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<IDigestRenderer>().ShouldNotBeNull();
        scope.ServiceProvider.GetService<IWeeklyRatingRenderer>().ShouldNotBeNull();
        scope.ServiceProvider.GetService<IReminderRenderer>().ShouldNotBeNull();
    }

    private sealed class EmptyCardDisplayQuery : ICardDisplayQuery
    {
        public Task<IReadOnlyDictionary<Guid, CardDisplayFacts>> DisplayFactsAsync(
            IReadOnlyCollection<Guid> jobIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, CardDisplayFacts>>(
                new Dictionary<Guid, CardDisplayFacts>());
    }
}
