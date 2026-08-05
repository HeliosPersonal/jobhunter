using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;

namespace JobHunter.Telegram.Transport;

/// <summary>
/// The single <see cref="INotifier"/> implementation (SAD §7). It sends one message at a time through the
/// <see cref="TelegramSendPacer"/>, which holds the sender inside Telegram's 30-messages-per-second limit,
/// and on a <c>429</c> it reads <c>parameters.retry_after</c>, penalises the pacer by exactly that delay and
/// retries — a provider cool-off is honoured to the second and never overridden by our own spacing. The bot
/// token lives only in the injected <see cref="HttpClient.BaseAddress"/> (<c>…/bot{token}/</c>); the request
/// path is the relative <c>sendMessage</c>, so the token appears in no log, no exception message and no span
/// (invariant 12). A send that exhausts its attempts is a <see cref="TelegramSendException"/> — an
/// infrastructure fault the caller (the delivery handler) surfaces, not a silent drop. A permanent
/// <c>4xx</c> refusal (a 400: bad chat, over-long message, malformed markup) is a
/// <see cref="NotificationRejectedException"/> instead, so the delivery loop can log that one card as failed
/// and deliver the rest (AC-05) rather than redeliver the whole digest.
/// </summary>
internal sealed class TelegramNotifier : INotifier
{
    /// <summary>The named client whose base address carries the bot token (never logged).</summary>
    public const string HttpClientName = "telegram-bot";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // A text-only message has no reply markup — omit the null so the payload is exactly what Telegram expects.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly TelegramSendPacer _pacer;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly int _maxAttempts;
    private readonly ILogger<TelegramNotifier> _logger;

    public TelegramNotifier(
        HttpClient http,
        TelegramSendPacer pacer,
        int maxAttempts,
        ILogger<TelegramNotifier> logger,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _pacer = pacer ?? throw new ArgumentNullException(nameof(pacer));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        _maxAttempts = maxAttempts;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _delay = delay ?? Task.Delay;
    }

    public async Task<long> SendAsync(long chatId, RenderedMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var payload = BuildPayload(chatId, message);

        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            // Wait out the pacer's slot before every attempt, so a retry after a 429 also respects the block.
            await _delay(_pacer.ReserveSlot(), cancellationToken).ConfigureAwait(false);

            using var response = await _http
                .PostAsJsonAsync("sendMessage", payload, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = await ReadRetryAfterAsync(response, cancellationToken).ConfigureAwait(false);
                _pacer.Penalise(retryAfter);
                _logger.LogWarning(
                    "Telegram returned 429 for chat {ChatId}; honouring retry_after of {RetryAfterSeconds}s (attempt {Attempt}/{MaxAttempts}).",
                    chatId, retryAfter.TotalSeconds, attempt, _maxAttempts);
                continue;
            }

            var body = await response.Content.ReadFromJsonAsync<TelegramResponse>(SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode && body is { Ok: true, Result.MessageId: var messageId })
            {
                return messageId;
            }

            // A 4xx (except the 429 handled above) is a permanent refusal of this message — a bad chat, a
            // message too long, malformed markup — that retrying will not fix. Surface it as a rejection so the
            // delivery loop logs the one card as failed and delivers the rest (AC-05), rather than a transient
            // fault that would propagate and redeliver the whole digest.
            if (IsPermanentRejection(response.StatusCode))
            {
                throw new NotificationRejectedException(
                    $"Telegram permanently rejected a send to chat {chatId} with status {(int)response.StatusCode} (ok={body?.Ok.ToString() ?? "null"}).");
            }

            throw new TelegramSendException(
                $"Telegram rejected a send to chat {chatId} with status {(int)response.StatusCode} (ok={body?.Ok.ToString() ?? "null"}).");
        }

        throw new TelegramSendException(
            $"Telegram send to chat {chatId} was throttled beyond {_maxAttempts} attempts.");
    }

    private static SendMessagePayload BuildPayload(long chatId, RenderedMessage message)
    {
        var keyboard = message.HasKeyboard
            ? new InlineKeyboardMarkup(message.Keyboard
                .Select(row => row.Select(b => new InlineKeyboardButton(b.Label, b.CallbackData)).ToArray())
                .ToArray())
            : null;

        return new SendMessagePayload(chatId, message.Text, "MarkdownV2", keyboard);
    }

    // A 4xx other than 429 is the message's fault, not the transport's — a retry sends the same bad request.
    private static bool IsPermanentRejection(HttpStatusCode status) =>
        (int)status is >= 400 and < 500 && status != HttpStatusCode.TooManyRequests;

    private static async Task<TimeSpan> ReadRetryAfterAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // The authoritative delay is in the JSON body's parameters.retry_after; the header is a fallback.
        var body = await response.Content.ReadFromJsonAsync<TelegramResponse>(SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        var seconds = body?.Parameters?.RetryAfter ?? response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 1;
        return TimeSpan.FromSeconds(Math.Max(1, seconds));
    }

    private sealed record SendMessagePayload(
        [property: JsonPropertyName("chat_id")] long ChatId,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("parse_mode")] string ParseMode,
        [property: JsonPropertyName("reply_markup")] InlineKeyboardMarkup? ReplyMarkup);

    private sealed record InlineKeyboardMarkup(
        [property: JsonPropertyName("inline_keyboard")] InlineKeyboardButton[][] InlineKeyboard);

    private sealed record InlineKeyboardButton(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("callback_data")] string CallbackData);

    private sealed record TelegramResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("result")] TelegramResult? Result,
        [property: JsonPropertyName("parameters")] TelegramResponseParameters? Parameters);

    private sealed record TelegramResult(
        [property: JsonPropertyName("message_id")] long MessageId);

    private sealed record TelegramResponseParameters(
        [property: JsonPropertyName("retry_after")] double? RetryAfter);
}
