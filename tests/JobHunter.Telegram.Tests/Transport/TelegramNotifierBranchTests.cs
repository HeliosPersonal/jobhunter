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
/// The notifier's residual defensive arms the primary <see cref="TelegramNotifierTests"/> does not reach — every one
/// a "the response looked fine but was not" or "the body was absent" case that only shows up when the coverage is
/// read arm by arm:
/// <list type="bullet">
/// <item>a 2xx whose body is <c>ok:false</c>, or is <c>ok:true</c> with no <c>result</c>, or is absent entirely —
/// each fails the success pattern and becomes a <see cref="TelegramSendException"/> rather than a phantom message id;</item>
/// <item>a permanent 4xx and a transient 5xx whose bodies are absent — the <c>ok=null</c> arm of the two rejection
/// messages, which the fixtures always populate with <c>ok:false</c>;</item>
/// <item>a 429 whose body is absent, and one whose <c>parameters</c> object is present but carries no
/// <c>retry_after</c> — the two null-conditional arms of the retry-after coalesce that the populated fixtures skip.</item>
/// </list>
/// All zero-network, with a recording no-op delay so nothing waits on real time, and every asserted message is
/// checked to never carry the bot token (invariant 12).
/// </summary>
public sealed class TelegramNotifierBranchTests
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

    // A response whose JSON body is the literal null — ReadFromJsonAsync yields a null TelegramResponse, the arm
    // that drives every body?.… null-conditional. (An empty body would throw rather than deserialize to null.)
    private static HttpResponseMessage NullBody(HttpStatusCode status) =>
        StubHttpMessageHandler.Json(status, "null");

    private static HttpResponseMessage Ok(long messageId = 555) =>
        StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            "{\"ok\":true,\"result\":{\"message_id\":" + messageId + "}}");

    [Fact]
    public async Task A_2xx_with_ok_false_is_not_a_success_and_is_a_send_fault()
    {
        // Success status, body present, but Ok is false: the success pattern fails on the Ok arm, and a 200 is not a
        // 4xx so it is not a permanent rejection — it surfaces as a transport fault, never a phantom message id.
        var harness = Build((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":false}"""));

        var ex = await Should.ThrowAsync<TelegramSendException>(() => harness.Notifier.SendAsync(4242, Header));

        ex.Message.ShouldContain("ok=False");
        ex.Message.ShouldNotContain(Token);
        harness.Handler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_2xx_that_is_ok_true_but_carries_no_result_is_a_send_fault()
    {
        // Ok is true but there is no result.message_id: the success pattern fails on the Result arm.
        var harness = Build((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":true}"""));

        var ex = await Should.ThrowAsync<TelegramSendException>(() => harness.Notifier.SendAsync(4242, Header));

        ex.Message.ShouldContain("ok=True");
        ex.Message.ShouldNotContain(Token);
    }

    [Fact]
    public async Task A_2xx_with_no_body_is_a_send_fault_with_a_null_ok()
    {
        // The body is absent, so the deserialized response is null: the success pattern fails on the null body, and
        // the fault message reports the ok=null coalesce arm.
        var harness = Build((_, _) => NullBody(HttpStatusCode.OK));

        var ex = await Should.ThrowAsync<TelegramSendException>(() => harness.Notifier.SendAsync(4242, Header));

        ex.Message.ShouldContain("ok=null");
        ex.Message.ShouldNotContain(Token);
    }

    [Fact]
    public async Task A_permanent_400_with_no_body_is_a_rejection_reporting_a_null_ok()
    {
        // A permanent rejection whose body is absent: the NotificationRejectedException's body?.Ok null-conditional
        // takes the "null" arm the populated {"ok":false} fixtures never reach.
        var harness = Build((_, _) => NullBody(HttpStatusCode.BadRequest));

        var ex = await Should.ThrowAsync<NotificationRejectedException>(() => harness.Notifier.SendAsync(4242, Header));

        ex.Message.ShouldContain("ok=null");
        ex.Message.ShouldNotContain(Token);
        harness.Handler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_transient_500_with_no_body_is_a_send_fault_reporting_a_null_ok()
    {
        // A transient fault whose body is absent: the TelegramSendException's body?.Ok null-conditional takes its
        // "null" arm — the counterpart of the permanent-rejection case above, on the non-4xx path.
        var harness = Build((_, _) => NullBody(HttpStatusCode.InternalServerError));

        var ex = await Should.ThrowAsync<TelegramSendException>(() => harness.Notifier.SendAsync(4242, Header));

        ex.Message.ShouldContain("ok=null");
        ex.Message.ShouldNotContain(Token);
        harness.Handler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_429_with_no_body_falls_back_to_the_default_cool_off()
    {
        // The retry-after body is absent (deserializes to null): the body?.Parameters null-conditional short-circuits
        // on the null body itself, and with no header the coalesce lands on the one-second default.
        var harness = Build((_, call) => call == 1
            ? NullBody(HttpStatusCode.TooManyRequests)
            : Ok(999));

        var messageId = await harness.Notifier.SendAsync(4242, Header);

        messageId.ShouldBe(999);
        harness.Delays.ShouldContain(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_429_whose_parameters_carry_no_retry_after_falls_back_to_the_header()
    {
        // The body and its parameters object are both present, but retry_after is absent: the RetryAfter arm of the
        // coalesce is null while Parameters is not, so the header supplies the delay.
        var harness = Build((_, call) =>
        {
            if (call > 1)
            {
                return Ok(222);
            }

            var response = StubHttpMessageHandler.Json(
                HttpStatusCode.TooManyRequests, """{"ok":false,"parameters":{}}""");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
            return response;
        });

        var messageId = await harness.Notifier.SendAsync(4242, Header);

        messageId.ShouldBe(222);
        harness.Delays.ShouldContain(TimeSpan.FromSeconds(3));
    }
}
