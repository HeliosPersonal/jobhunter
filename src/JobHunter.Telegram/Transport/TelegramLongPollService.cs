using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace JobHunter.Telegram.Transport;

/// <summary>
/// The single-consumer long-poll loop (SAD §7): one replica, <c>strategy: Recreate</c>, so exactly one
/// process pulls updates — two would each get half, presenting as randomly-ignored taps. It calls
/// <c>getUpdates</c> with a long timeout, hands each update to <see cref="ITelegramUpdateProcessor"/>
/// (which fronts the <see cref="Auth.OwnerAuthorizer"/> allowlist), and only advances the acknowledged
/// offset past an update once it has been processed — so a crash mid-batch reprocesses rather than skips,
/// and a network interruption reconnects from the last acknowledged offset without losing updates. A
/// transient failure is logged and retried after <see cref="TelegramOptions.ReconnectDelay"/>; the loop
/// never dies on a poll error.
/// </summary>
internal sealed class TelegramLongPollService(
    IHttpClientFactory httpClientFactory,
    ITelegramUpdateProcessor processor,
    IOptions<TelegramOptions> options,
    ILogger<TelegramLongPollService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory =
        httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly ITelegramUpdateProcessor _processor = processor ?? throw new ArgumentNullException(nameof(processor));
    private readonly TelegramOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<TelegramLongPollService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        long offset = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                offset = await PollOnceAsync(offset, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown — the pod is stopping, not an error.
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                // A network interruption or a slow poll: wait and reconnect from the same offset, losing nothing.
                _logger.LogWarning(ex, "Long-poll interrupted; reconnecting in {DelaySeconds}s.", _options.ReconnectDelay.TotalSeconds);
                await Task.Delay(_options.ReconnectDelay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// One <c>getUpdates</c> round. Returns the offset to poll from next: the highest update id seen plus
    /// one, or the unchanged offset when nothing arrived. An update is processed before its id advances the
    /// offset, so nothing is acknowledged until it is handled.
    /// </summary>
    internal async Task<long> PollOnceAsync(long offset, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(TelegramNotifier.HttpClientName);
        var url = $"getUpdates?timeout={_options.LongPollTimeoutSeconds}&offset={offset}";

        var response = await client.GetFromJsonAsync<GetUpdatesResponse>(url, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        var updates = response?.Result ?? [];
        var next = offset;

        foreach (var update in updates)
        {
            await _processor.ProcessAsync(update, cancellationToken).ConfigureAwait(false);
            next = update.UpdateId + 1;
        }

        return next;
    }

    internal sealed record GetUpdatesResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("result")] IReadOnlyList<TelegramUpdate> Result);
}
