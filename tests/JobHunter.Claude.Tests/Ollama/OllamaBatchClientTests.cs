using System.Net;
using JobHunter.Claude.Enrichment;
using JobHunter.Claude.Ollama;
using JobHunter.Claude.Tests.Support;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Ollama;

/// <summary>
/// T14: the <see cref="OllamaBatchClient"/> against recorded Ollama payloads with zero network (SAD §3).
/// It asserts the whole port contract holds identically to the Anthropic tier — submit synthesises a
/// batch and returns its id, status reports it ended, results stream item by item, a per-item transport
/// fault is a value not an exception, the enrichment parses through the same tolerant parser, and a lost
/// batch degrades availability gracefully rather than hanging a Run.
/// </summary>
public sealed class OllamaBatchClientTests
{
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ollama");
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 2, 0, 0, TimeSpan.Zero);

    private static readonly OllamaOptions Options = new()
    {
        BaseUrl = "http://ollama.helios.test:11434",
        Model = "llama3.1:8b",
        MaxOutputTokens = 1024,
    };

    private static OllamaBatchClient NewClient(StubHttpMessageHandler handler, IOllamaResultStore store, FakeClock? clock = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri(Options.BaseUrl) };
        return new OllamaBatchClient(
            http,
            Microsoft.Extensions.Options.Options.Create(Options),
            store,
            clock ?? new FakeClock(Now),
            new SequentialIdGenerator(),
            NullLogger<OllamaBatchClient>.Instance);
    }

    private static BatchSubmission SampleSubmission(params string[] customIds)
    {
        var schema = new JsonSchema("enrichment", """{"type":"object","required":["reasons"]}""");
        var items = customIds
            .Select(id => new BatchRequestItem(id, "system", $"job {id}", schema))
            .ToArray();
        return new BatchSubmission(ModelTier.Cheap, "enrich-v1", items);
    }

    [Fact]
    public async Task Submit_runs_each_item_synchronously_and_returns_a_batch_id()
    {
        var handler = StubHttpMessageHandler.Always(await File.ReadAllTextAsync(Path.Combine(FixtureDir, "chat-success.json")));
        var store = new InMemoryOllamaResultStore();
        var client = NewClient(handler, store);

        var batchId = await client.SubmitAsync(SampleSubmission("job-1", "job-2"), CancellationToken.None);

        batchId.ShouldStartWith("ollama-batch-");
        handler.CallCount.ShouldBe(2);
        handler.Requests[0].Method.ShouldBe(HttpMethod.Post);
        handler.Requests[0].Uri!.AbsolutePath.ShouldBe("/api/chat");
        handler.Requests[0].Body!.ShouldContain("\"model\":\"llama3.1:8b\"");
    }

    [Fact]
    public async Task Status_reports_ended_immediately_with_the_success_and_error_counts()
    {
        var handler = new StubHttpMessageHandler((_, call) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(call == 1
                ? File.ReadAllText(Path.Combine(FixtureDir, "chat-success.json"))
                : File.ReadAllText(Path.Combine(FixtureDir, "chat-no-content.json"))),
        });
        var store = new InMemoryOllamaResultStore();
        var client = NewClient(handler, store);

        var batchId = await client.SubmitAsync(SampleSubmission("job-1", "job-2"), CancellationToken.None);
        var status = await client.GetStatusAsync(batchId, CancellationToken.None);

        status.State.ShouldBe(ProviderBatchState.Ended);
        status.Succeeded.ShouldBe(1);
        status.Errored.ShouldBe(1);
        status.Processing.ShouldBe(0);
    }

    [Fact]
    public async Task Results_stream_the_stored_items_and_parse_through_the_same_tolerant_parser()
    {
        var handler = StubHttpMessageHandler.Always(await File.ReadAllTextAsync(Path.Combine(FixtureDir, "chat-success.json")));
        var store = new InMemoryOllamaResultStore();
        var client = NewClient(handler, store);

        var batchId = await client.SubmitAsync(SampleSubmission("job-1"), CancellationToken.None);

        var items = new List<BatchResultItem>();
        await foreach (var item in client.GetResultsAsync(batchId, CancellationToken.None))
        {
            items.Add(item);
        }

        items.Count.ShouldBe(1);
        var first = items[0];
        first.CustomId.ShouldBe("job-1");
        first.ProviderError.ShouldBeNull();
        first.RawJson.ShouldNotBeNull();
        first.Usage.InputTokens.ShouldBe(3800);
        first.Usage.OutputTokens.ShouldBe(260);

        // The recorded output flows through the very parser production uses, proving the fallback emits the
        // same wire shape the Anthropic tier does (done-when: parses through the same TolerantJsonParser).
        var outcome = new EnrichmentResultParser().Parse(new EnrichmentParseRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "enrich-v1", Now, first.RawJson));
        outcome.IsSuccess.ShouldBeTrue();
        outcome.Enrichment!.AiUsage.ShouldBe(JobHunter.Domain.Intelligence.AiUsageLevel.High);
        outcome.Enrichment.Reasons.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_response_without_message_content_is_a_recorded_provider_error_not_an_exception()
    {
        var handler = StubHttpMessageHandler.Always(await File.ReadAllTextAsync(Path.Combine(FixtureDir, "chat-no-content.json")));
        var store = new InMemoryOllamaResultStore();
        var client = NewClient(handler, store);

        var batchId = await client.SubmitAsync(SampleSubmission("job-1"), CancellationToken.None);

        var items = new List<BatchResultItem>();
        await foreach (var item in client.GetResultsAsync(batchId, CancellationToken.None))
        {
            items.Add(item);
        }

        items[0].RawJson.ShouldBeNull();
        items[0].ProviderError.ShouldNotBeNull();
        items[0].ProviderError!.ShouldContain("no message content");
    }

    [Fact]
    public async Task A_non_success_status_is_a_recorded_provider_error()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("down") });
        var store = new InMemoryOllamaResultStore();
        var client = NewClient(handler, store);

        var batchId = await client.SubmitAsync(SampleSubmission("job-1"), CancellationToken.None);

        var items = new List<BatchResultItem>();
        await foreach (var item in client.GetResultsAsync(batchId, CancellationToken.None))
        {
            items.Add(item);
        }

        items[0].ProviderError.ShouldNotBeNull();
        items[0].ProviderError!.ShouldContain("503");
    }

    [Fact]
    public async Task A_transport_fault_on_one_item_is_recorded_never_thrown()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("connection refused"));
        var store = new InMemoryOllamaResultStore();
        var client = NewClient(handler, store);

        // Submit must not throw even though Ollama is unreachable: availability is what the fallback guards.
        var batchId = await client.SubmitAsync(SampleSubmission("job-1"), CancellationToken.None);

        var items = new List<BatchResultItem>();
        await foreach (var item in client.GetResultsAsync(batchId, CancellationToken.None))
        {
            items.Add(item);
        }

        items[0].RawJson.ShouldBeNull();
        items[0].ProviderError.ShouldNotBeNull();
        items[0].ProviderError!.ShouldContain("transport fault");
    }

    [Fact]
    public async Task A_batch_this_process_never_ran_is_reported_expired_so_the_poller_carries_over()
    {
        var handler = StubHttpMessageHandler.Always("{}");
        var store = new InMemoryOllamaResultStore();
        var client = NewClient(handler, store);

        var status = await client.GetStatusAsync("ollama-batch-unknown", CancellationToken.None);

        status.State.ShouldBe(ProviderBatchState.Expired);
    }

    [Fact]
    public async Task List_recent_batches_returns_synthesised_batches_created_on_or_after_the_bound()
    {
        var handler = StubHttpMessageHandler.Always(await File.ReadAllTextAsync(Path.Combine(FixtureDir, "chat-success.json")));
        var store = new InMemoryOllamaResultStore();
        var clock = new FakeClock(Now);
        var client = NewClient(handler, store, clock);

        var earlier = await client.SubmitAsync(SampleSubmission("job-early"), CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(10));
        var bound = clock.UtcNow;
        var later = await client.SubmitAsync(SampleSubmission("job-late"), CancellationToken.None);

        var refs = await client.ListRecentBatchesAsync(bound, CancellationToken.None);

        refs.Select(r => r.ProviderBatchId).ShouldBe([later]);
        refs.ShouldNotContain(r => r.ProviderBatchId == earlier);
    }
}
