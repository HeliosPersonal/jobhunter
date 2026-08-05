using System.Net;
using JobHunter.Telegram.Tests.Support;
using JobHunter.Telegram.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Transport;

/// <summary>
/// The single-consumer long-poll loop (T07). The load-bearing rules: an update is processed before its id
/// advances the acknowledged offset (a crash mid-batch reprocesses rather than skips); an empty poll leaves
/// the offset unchanged; and a network interruption reconnects from the last offset without losing updates.
/// The loop is driven against a stub handler with zero network and a near-zero reconnect delay.
/// </summary>
public sealed class TelegramLongPollServiceTests
{
    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.telegram.org/bottoken/") };
    }

    private static TelegramLongPollService Build(
        HttpMessageHandler handler, ITelegramUpdateProcessor processor, TimeSpan? reconnect = null)
    {
        var options = Options.Create(new TelegramOptions
        {
            BotToken = "token",
            AllowedChatIds = [1],
            LongPollTimeoutSeconds = 1,
            ReconnectDelay = reconnect ?? TimeSpan.FromMilliseconds(10),
        });
        return new TelegramLongPollService(
            new SingleClientFactory(handler), processor, options, NullLogger<TelegramLongPollService>.Instance);
    }

    private static HttpResponseMessage Updates(params long[] updateIds)
    {
        var results = string.Join(",", updateIds.Select(id =>
            "{\"update_id\":" + id + ",\"message\":{\"chat\":{\"id\":1},\"text\":\"hi\"}}"));
        return StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":[" + results + "]}");
    }

    [Fact]
    public async Task Each_update_is_processed_and_the_offset_advances_past_the_highest_id()
    {
        var processor = new RecordingUpdateProcessor();
        var handler = new StubHttpMessageHandler((_, _) => Updates(10, 11, 12));
        var service = Build(handler, processor);

        var next = await service.PollOnceAsync(0, CancellationToken.None);

        next.ShouldBe(13);
        processor.Processed.ShouldBe([10L, 11L, 12L]);
    }

    [Fact]
    public async Task The_poll_url_carries_the_current_offset_and_the_long_poll_timeout()
    {
        var handler = new StubHttpMessageHandler((_, _) => Updates());
        var service = Build(handler, new RecordingUpdateProcessor());

        await service.PollOnceAsync(42, CancellationToken.None);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Uri!.Query.ShouldContain("offset=42");
        request.Uri.Query.ShouldContain("timeout=1");
    }

    [Fact]
    public async Task An_empty_poll_leaves_the_offset_unchanged()
    {
        var processor = new RecordingUpdateProcessor();
        var handler = new StubHttpMessageHandler((_, _) => Updates());
        var service = Build(handler, processor);

        var next = await service.PollOnceAsync(7, CancellationToken.None);

        next.ShouldBe(7);
        processor.Processed.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_network_interruption_reconnects_and_loses_no_updates()
    {
        // First poll throws (a dropped connection); the loop must retry and then deliver the update.
        var processor = new RecordingUpdateProcessor();
        var handler = new StubHttpMessageHandler((_, call) => call == 1
            ? throw new HttpRequestException("connection reset")
            : Updates(99));
        var service = Build(handler, processor);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        // Give the loop time to fail, reconnect and deliver, then stop it.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (processor.Processed.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, cts.Token);
        }

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        processor.Processed.ShouldContain(99L);
        handler.CallCount.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void A_null_http_client_factory_is_rejected()
    {
        var options = Options.Create(new TelegramOptions { BotToken = "t", AllowedChatIds = [1] });

        Should.Throw<ArgumentNullException>(() => new TelegramLongPollService(
            null!, new RecordingUpdateProcessor(), options, NullLogger<TelegramLongPollService>.Instance));
    }

    [Fact]
    public void A_null_processor_is_rejected()
    {
        var options = Options.Create(new TelegramOptions { BotToken = "t", AllowedChatIds = [1] });

        Should.Throw<ArgumentNullException>(() => new TelegramLongPollService(
            new SingleClientFactory(new StubHttpMessageHandler((_, _) => Updates())),
            null!, options, NullLogger<TelegramLongPollService>.Instance));
    }

    [Fact]
    public void A_null_options_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => new TelegramLongPollService(
            new SingleClientFactory(new StubHttpMessageHandler((_, _) => Updates())),
            new RecordingUpdateProcessor(), null!, NullLogger<TelegramLongPollService>.Instance));
    }

    [Fact]
    public void A_null_logger_is_rejected()
    {
        var options = Options.Create(new TelegramOptions { BotToken = "t", AllowedChatIds = [1] });

        Should.Throw<ArgumentNullException>(() => new TelegramLongPollService(
            new SingleClientFactory(new StubHttpMessageHandler((_, _) => Updates())),
            new RecordingUpdateProcessor(), options, null!));
    }

    [Fact]
    public async Task Cancellation_stops_the_loop_without_faulting()
    {
        // A poll that observes the stopping token throws OperationCanceledException; that is a graceful stop.
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new TaskCanceledException("cancelled", new TimeoutException()));
        var service = Build(handler, new RecordingUpdateProcessor(), reconnect: TimeSpan.FromMilliseconds(5));

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(30, CancellationToken.None);
        await cts.CancelAsync();

        // StopAsync completes cleanly — the background task did not fault on the cancellation.
        await Should.NotThrowAsync(() => service.StopAsync(CancellationToken.None));
    }
}
