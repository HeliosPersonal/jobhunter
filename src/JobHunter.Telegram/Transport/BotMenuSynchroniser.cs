using System.Text.Json;
using System.Text.Json.Serialization;
using JobHunter.Application.Commands;

namespace JobHunter.Telegram.Transport;

/// <summary>
/// Pushes the registry-derived client menu to Telegram at startup (AC-01, SAD §4 S5). It posts the
/// <see cref="BotMenu"/> entries — the same list the router dispatches on — to <c>setMyCommands</c>, so the
/// menu the Owner sees is generated from the command surface and cannot drift from it. As with the notifier,
/// the bot token lives only on the injected <see cref="HttpClient.BaseAddress"/> (<c>…/bot{token}/</c>) and
/// the request path is the relative <c>setMyCommands</c>, so the token appears in no log and no span
/// (invariant 12). A refusal is a <see cref="TelegramSendException"/>, surfaced rather than swallowed: a
/// bot whose menu silently failed to publish is a bug the host should see at boot.
/// </summary>
internal sealed class BotMenuSynchroniser
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IReadOnlyList<BotMenuEntry> _menu;
    private readonly ILogger<BotMenuSynchroniser> _logger;

    public BotMenuSynchroniser(
        HttpClient http, IReadOnlyList<BotMenuEntry> menu, ILogger<BotMenuSynchroniser> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _menu = menu ?? throw new ArgumentNullException(nameof(menu));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Publishes the menu to Telegram; throws on a refusal so a boot-time failure is visible.</summary>
    public async Task SynchroniseAsync(CancellationToken cancellationToken = default)
    {
        var payload = new SetMyCommandsPayload(
            _menu.Select(e => new BotCommandPayload(e.Command, e.Description)).ToArray());

        using var response = await _http
            .PostAsJsonAsync("setMyCommands", payload, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new TelegramSendException(
                $"Telegram rejected setMyCommands with status {(int)response.StatusCode}.");
        }

        _logger.LogInformation("Synchronised {CommandCount} commands to the Telegram client menu.", _menu.Count);
    }

    private sealed record SetMyCommandsPayload(
        [property: JsonPropertyName("commands")] BotCommandPayload[] Commands);

    private sealed record BotCommandPayload(
        [property: JsonPropertyName("command")] string Command,
        [property: JsonPropertyName("description")] string Description);
}
