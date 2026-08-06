using System.Net;
using JobHunter.Domain.Notifications;
using JobHunter.TestKit;
using JobHunter.Telegram.Tests.Support;
using JobHunter.Telegram.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Transport;

/// <summary>
/// The one <see cref="Domain.Abstractions.INotifier"/> implementation (T07), driven against a stub handler
/// with zero network and a recording no-op delay so nothing waits on real time. The load-bearing rules: a
/// 200 returns the Telegram message id; a 429 is retried honouring <c>retry_after</c> exactly; the bot token
/// (carried on the base address) never appears in a request path; a keyboard is sent as
/// <c>inline_keyboard</c>; and a rejected send is a <see cref="TelegramSendException"/>, never a silent drop.
/// </summary>
public sealed class TelegramNotifierTests
{
    private const string Token = "123456:AAsecretbottoken";
    private static readonly Uri BaseAddress = new($"https://api.telegram.org/bot{Token}/");

    private static readonly RenderedMessage Header = RenderedMessage.PlainText("🌅 *Good morning*");

    private sealed record Harness(TelegramNotifier Notifier, StubHttpMessageHandler Handler, List<TimeSpan> Delays);

    private static Harness Build(Func<HttpRequestMessage, int, HttpResponseMessage> respond, int maxAttempts = 5)
    {
        var handler = new StubHttpMessageHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = BaseAddress };
        var pacer = new TelegramSendPacer(new FakeClock(), TimeSpan.FromMilliseconds(50));
        var delays = new List<TimeSpan>();
        var notifier = new TelegramNotifier(
            http, pacer, maxAttempts, NullLogger<TelegramNotifier>.Instance,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });
        return new Harness(notifier, handler, delays);
    }

    private static HttpResponseMessage Ok(long messageId = 555) =>
        StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            "{\"ok\":true,\"result\":{\"message_id\":" + messageId + "}}");

    [Fact]
    public async Task A_successful_send_returns_the_telegram_message_id()
    {
        var harness = Build((_, _) => Ok(777));

        var messageId = await harness.Notifier.SendAsync(4242, Header);

        messageId.ShouldBe(777);
        harness.Handler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task The_send_posts_the_relative_sendMessage_endpoint()
    {
        var harness = Build((_, _) => Ok());

        await harness.Notifier.SendAsync(4242, Header);

        var request = harness.Handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        // The adapter builds a token-free relative path; the token is supplied by the injected base address,
        // never constructed here (so it never reaches a log or a span the adapter emits — invariant 12).
        request.Uri!.AbsoluteUri.ShouldEndWith("/sendMessage");
        request.Body.ShouldNotBeNull().ShouldNotContain(Token);
    }

    [Fact]
    public async Task The_payload_carries_the_chat_id_markdownv2_and_the_text()
    {
        var harness = Build((_, _) => Ok());

        await harness.Notifier.SendAsync(4242, Header);

        var body = harness.Handler.Requests.ShouldHaveSingleItem().Body!;
        body.ShouldContain("\"chat_id\":4242");
        body.ShouldContain("MarkdownV2");
        body.ShouldContain("Good morning");
    }

    [Fact]
    public async Task A_message_with_a_keyboard_sends_an_inline_keyboard()
    {
        var harness = Build((_, _) => Ok());
        var card = new RenderedMessage(
            "*Staff Engineer*",
            [[new InlineButton("Save", "save:ab12"), new InlineButton("Ignore", "ignore:ab12")]]);

        await harness.Notifier.SendAsync(4242, card);

        var body = harness.Handler.Requests.ShouldHaveSingleItem().Body!;
        body.ShouldContain("inline_keyboard");
        body.ShouldContain("callback_data");
        body.ShouldContain("ignore:ab12");
    }

    [Fact]
    public async Task A_text_only_message_omits_the_reply_markup()
    {
        var harness = Build((_, _) => Ok());

        await harness.Notifier.SendAsync(4242, Header);

        harness.Handler.Requests.ShouldHaveSingleItem().Body!.ShouldNotContain("reply_markup");
    }

    [Fact]
    public async Task An_open_button_is_sent_as_a_url_not_a_callback()
    {
        var harness = Build((_, _) => Ok());
        // The Open action is a link, not a callback: Telegram opens it directly, so it carries a url and
        // never a callback_data — the bot never sees a tap on it (contract §Callback payloads, Open row).
        var card = new RenderedMessage(
            "*Staff Engineer*",
            [[InlineButton.ForUrl("Open", "https://acme.com/apply/1"), new InlineButton("Save", "sav:ab12")]]);

        await harness.Notifier.SendAsync(4242, card);

        var body = harness.Handler.Requests.ShouldHaveSingleItem().Body!;
        body.ShouldContain("\"url\":\"https://acme.com/apply/1\"");
        body.ShouldContain("\"callback_data\":\"sav:ab12\"");
    }

    [Fact]
    public async Task Answering_a_callback_query_posts_the_relative_endpoint_with_id_and_text()
    {
        var harness = Build((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":true}"));

        await harness.Notifier.AnswerCallbackAsync("cb42", "Saved");

        var request = harness.Handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri!.AbsoluteUri.ShouldEndWith("/answerCallbackQuery");
        request.Body.ShouldNotBeNull();
        request.Body.ShouldContain("\"callback_query_id\":\"cb42\"");
        request.Body.ShouldContain("Saved");
        // The token lives only on the base address; an ack path never carries it (invariant 12).
        request.Body.ShouldNotContain(Token);
    }

    [Fact]
    public async Task Answering_a_callback_query_with_no_text_omits_the_text()
    {
        var harness = Build((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":true}"));

        // The Open action has no acknowledgement text — the URL button opens directly (contract, Open row).
        await harness.Notifier.AnswerCallbackAsync("cb42", text: null);

        harness.Handler.Requests.ShouldHaveSingleItem().Body!.ShouldNotContain("\"text\"");
    }

    [Fact]
    public async Task A_rejected_callback_answer_is_a_send_fault_not_a_silent_drop()
    {
        var harness = Build((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.BadRequest, "{\"ok\":false}"));

        var ex = await Should.ThrowAsync<TelegramSendException>(() => harness.Notifier.AnswerCallbackAsync("cb42", "Saved"));

        ex.Message.ShouldNotContain(Token);
    }

    [Fact]
    public async Task Editing_a_reply_markup_posts_the_chat_message_and_keyboard()
    {
        var harness = Build((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":{\"message_id\":555}}"));

        await harness.Notifier.EditReplyMarkupAsync(4242, 555, [[new InlineButton("Ignored", "noop")]]);

        var request = harness.Handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri!.AbsoluteUri.ShouldEndWith("/editMessageReplyMarkup");
        var body = request.Body.ShouldNotBeNull();
        body.ShouldContain("\"chat_id\":4242");
        body.ShouldContain("\"message_id\":555");
        body.ShouldContain("inline_keyboard");
        body.ShouldContain("Ignored");
        body.ShouldNotContain(Token);
    }

    [Fact]
    public async Task A_rejected_reply_markup_edit_is_a_send_fault()
    {
        var harness = Build((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.BadRequest, "{\"ok\":false}"));

        var ex = await Should.ThrowAsync<TelegramSendException>(() =>
            harness.Notifier.EditReplyMarkupAsync(4242, 555, [[new InlineButton("Ignored", "noop")]]));

        ex.Message.ShouldNotContain(Token);
    }

    [Fact]
    public async Task A_null_or_empty_callback_query_id_is_rejected()
    {
        var harness = Build((_, _) => Ok());

        await Should.ThrowAsync<ArgumentException>(() => harness.Notifier.AnswerCallbackAsync("", "Saved"));
    }

    [Fact]
    public async Task A_429_is_retried_and_the_retry_after_is_honoured_exactly()
    {
        // First attempt 429 with retry_after=7; second attempt succeeds.
        var harness = Build((_, call) => call == 1
            ? StubHttpMessageHandler.Json(
                HttpStatusCode.TooManyRequests,
                """{"ok":false,"error_code":429,"parameters":{"retry_after":7}}""")
            : Ok(888));

        var messageId = await harness.Notifier.SendAsync(4242, Header);

        messageId.ShouldBe(888);
        harness.Handler.CallCount.ShouldBe(2);
        // The delay before the retry is the pacer's slot, which the 7-second penalty pushed out to exactly 7s.
        harness.Delays.ShouldContain(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task A_429_without_a_body_retry_after_falls_back_to_the_header()
    {
        var harness = Build((_, call) =>
        {
            if (call > 1)
            {
                return Ok(321);
            }

            var response = StubHttpMessageHandler.Json(
                HttpStatusCode.TooManyRequests, """{"ok":false,"error_code":429}""");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(4));
            return response;
        });

        var messageId = await harness.Notifier.SendAsync(4242, Header);

        messageId.ShouldBe(321);
        harness.Delays.ShouldContain(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task A_429_with_no_delay_anywhere_defaults_to_a_one_second_cool_off()
    {
        var harness = Build((_, call) => call == 1
            ? StubHttpMessageHandler.Json(HttpStatusCode.TooManyRequests, """{"ok":false}""")
            : Ok(654));

        var messageId = await harness.Notifier.SendAsync(4242, Header);

        messageId.ShouldBe(654);
        harness.Delays.ShouldContain(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_send_throttled_past_its_attempt_budget_fails()
    {
        var harness = Build(
            (_, _) => StubHttpMessageHandler.Json(
                HttpStatusCode.TooManyRequests,
                """{"ok":false,"parameters":{"retry_after":1}}"""),
            maxAttempts: 3);

        await Should.ThrowAsync<TelegramSendException>(() => harness.Notifier.SendAsync(4242, Header));

        harness.Handler.CallCount.ShouldBe(3);
    }

    [Fact]
    public async Task A_permanent_400_is_a_rejection_not_a_silent_drop()
    {
        var harness = Build((_, _) =>
            StubHttpMessageHandler.Json(HttpStatusCode.BadRequest, """{"ok":false,"error_code":400}"""));

        // A 400 is the message's fault, not the transport's: a rejection the delivery loop logs as a failed
        // card and moves past, never retried. The failure message never carries the token (invariant 12).
        var ex = await Should.ThrowAsync<NotificationRejectedException>(() => harness.Notifier.SendAsync(4242, Header));

        ex.Message.ShouldNotContain(Token);
        harness.Handler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_server_error_is_a_transport_fault_that_propagates_for_retry()
    {
        var harness = Build((_, _) =>
            StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, """{"ok":false,"error_code":500}"""));

        // A 5xx is the transport's fault: a TelegramSendException the caller surfaces so the message is retried,
        // not a per-card rejection that would drop the card.
        var ex = await Should.ThrowAsync<TelegramSendException>(() => harness.Notifier.SendAsync(4242, Header));

        ex.Message.ShouldNotContain(Token);
        harness.Handler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_null_message_is_rejected()
    {
        var harness = Build((_, _) => Ok());

        await Should.ThrowAsync<ArgumentNullException>(() => harness.Notifier.SendAsync(4242, null!));
    }

    [Fact]
    public void A_non_positive_attempt_budget_is_rejected()
    {
        var http = new HttpClient { BaseAddress = BaseAddress };
        var pacer = new TelegramSendPacer(new FakeClock(), TimeSpan.FromMilliseconds(50));

        Should.Throw<ArgumentOutOfRangeException>(() =>
            new TelegramNotifier(http, pacer, 0, NullLogger<TelegramNotifier>.Instance));
    }

    [Fact]
    public void A_null_http_client_is_rejected()
    {
        var pacer = new TelegramSendPacer(new FakeClock(), TimeSpan.FromMilliseconds(50));

        Should.Throw<ArgumentNullException>(() =>
            new TelegramNotifier(null!, pacer, 1, NullLogger<TelegramNotifier>.Instance));
    }

    [Fact]
    public void A_null_pacer_is_rejected()
    {
        var http = new HttpClient { BaseAddress = BaseAddress };

        Should.Throw<ArgumentNullException>(() =>
            new TelegramNotifier(http, null!, 1, NullLogger<TelegramNotifier>.Instance));
    }

    [Fact]
    public void A_null_logger_is_rejected()
    {
        var http = new HttpClient { BaseAddress = BaseAddress };
        var pacer = new TelegramSendPacer(new FakeClock(), TimeSpan.FromMilliseconds(50));

        Should.Throw<ArgumentNullException>(() =>
            new TelegramNotifier(http, pacer, 1, null!));
    }

    [Fact]
    public void The_default_delay_is_the_real_task_delay_when_none_is_injected()
    {
        var http = new HttpClient { BaseAddress = BaseAddress };
        var pacer = new TelegramSendPacer(new FakeClock(), TimeSpan.FromMilliseconds(50));

        // Constructing without a delay seam falls back to Task.Delay; we only assert it constructs, never send.
        var notifier = new TelegramNotifier(http, pacer, 1, NullLogger<TelegramNotifier>.Instance);

        notifier.ShouldNotBeNull();
    }
}
