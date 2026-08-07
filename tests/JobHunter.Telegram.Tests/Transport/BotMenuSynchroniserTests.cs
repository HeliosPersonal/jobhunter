using System.Net;
using System.Text.Json;
using JobHunter.Application.Commands;
using JobHunter.Telegram.Tests.Support;
using JobHunter.Telegram.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Transport;

/// <summary>
/// The <see cref="BotMenuSynchroniser"/> (AC-01): at startup it pushes the registry-derived menu to Telegram
/// through <c>setMyCommands</c>, so the client menu is generated from the surface and cannot drift. Driven
/// against the stub handler with zero network — the load-bearing rules are that the payload carries exactly
/// the menu entries in order, that the bot token (on the base address) never appears in the request path,
/// and that a Telegram refusal is surfaced, not swallowed.
/// </summary>
public sealed class BotMenuSynchroniserTests
{
    private const string Token = "123456:AAsecretbottoken";
    private static readonly Uri BaseAddress = new($"https://api.telegram.org/bot{Token}/");

    private static readonly IReadOnlyList<BotMenuEntry> Menu =
    [
        new("digest", "Re-read today's digest"),
        new("run", "Trigger the daily pipeline"),
    ];

    private static BotMenuSynchroniser Build(
        StubHttpMessageHandler handler, IReadOnlyList<BotMenuEntry> menu)
    {
        var http = new HttpClient(handler) { BaseAddress = BaseAddress };
        return new BotMenuSynchroniser(http, menu, NullLogger<BotMenuSynchroniser>.Instance);
    }

    private static HttpResponseMessage Ok() =>
        StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":true}");

    [Fact]
    public async Task Posts_the_relative_setMyCommands_endpoint_so_the_token_stays_on_the_base_address()
    {
        var handler = new StubHttpMessageHandler((_, _) => Ok());

        await Build(handler, Menu).SynchroniseAsync();

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        // The code supplies only the relative path; the token rides the base address, so it is never
        // something we append to a path, a log or a span (invariant 12) — and never in the body.
        request.Uri!.AbsoluteUri.ShouldEndWith("/setMyCommands");
        request.Body.ShouldNotBeNull().ShouldNotContain(Token);
    }

    [Fact]
    public async Task The_payload_carries_exactly_the_menu_entries_in_order()
    {
        var handler = new StubHttpMessageHandler((_, _) => Ok());

        await Build(handler, Menu).SynchroniseAsync();

        using var document = JsonDocument.Parse(handler.Requests.Single().Body!);
        var commands = document.RootElement.GetProperty("commands");

        commands.GetArrayLength().ShouldBe(2);
        commands[0].GetProperty("command").GetString().ShouldBe("digest");
        commands[0].GetProperty("description").GetString().ShouldBe("Re-read today's digest");
        commands[1].GetProperty("command").GetString().ShouldBe("run");
        commands[1].GetProperty("description").GetString().ShouldBe("Trigger the daily pipeline");
    }

    [Fact]
    public async Task A_telegram_refusal_is_surfaced_rather_than_swallowed()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            StubHttpMessageHandler.Json(HttpStatusCode.BadRequest, "{\"ok\":false}"));

        await Should.ThrowAsync<TelegramSendException>(() => Build(handler, Menu).SynchroniseAsync());
    }

    [Fact]
    public void Rejects_a_null_http_client() =>
        Should.Throw<ArgumentNullException>(() =>
            new BotMenuSynchroniser(null!, Menu, NullLogger<BotMenuSynchroniser>.Instance));

    [Fact]
    public void Rejects_a_null_menu() =>
        Should.Throw<ArgumentNullException>(() =>
            new BotMenuSynchroniser(new HttpClient { BaseAddress = BaseAddress }, null!,
                NullLogger<BotMenuSynchroniser>.Instance));

    [Fact]
    public void Rejects_a_null_logger() =>
        Should.Throw<ArgumentNullException>(() =>
            new BotMenuSynchroniser(new HttpClient { BaseAddress = BaseAddress }, Menu, null!));
}
