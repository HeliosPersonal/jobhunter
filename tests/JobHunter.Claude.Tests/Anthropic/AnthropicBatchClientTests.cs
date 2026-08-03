using System.Net;
using JobHunter.Claude;
using JobHunter.Claude.Anthropic;
using JobHunter.Claude.Tests.Support;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Anthropic;

/// <summary>
/// T07: the <see cref="AnthropicBatchClient"/> against recorded payloads with zero network (SAD §5). It
/// asserts the whole port contract — submit returns the provider batch id, status maps onto the
/// provider-agnostic vocabulary, results stream item by item, a per-item provider error is a value not an
/// exception, a 4xx does not retry, and the API key never appears in an exception message (invariant 12).
/// </summary>
public sealed class AnthropicBatchClientTests
{
    private const string ApiKey = "sk-ant-secret-key-value";
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "anthropic");

    private static readonly PricingOptions Pricing = new()
    {
        Tiers = new Dictionary<string, TierPricing>
        {
            ["Cheap"] = new() { ModelId = "claude-haiku-4-5", InputPerMillion = 1.00m, OutputPerMillion = 5.00m, BatchDiscount = 0.5m },
            ["Deep"] = new() { ModelId = "claude-sonnet-5", InputPerMillion = 3.00m, OutputPerMillion = 15.00m, BatchDiscount = 0.5m },
        },
    };

    private static readonly AnthropicOptions Options = new()
    {
        ApiKey = ApiKey,
        BaseUrl = "https://api.anthropic.test",
        ApiVersion = "2023-06-01",
        MaxOutputTokens = 1024,
    };

    private static AnthropicBatchClient NewClient(StubHttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri(Options.BaseUrl) };
        return new AnthropicBatchClient(
            http,
            Microsoft.Extensions.Options.Options.Create(Pricing),
            Microsoft.Extensions.Options.Options.Create(Options),
            NullLogger<AnthropicBatchClient>.Instance);
    }

    private static BatchSubmission SampleSubmission()
    {
        var schema = new JsonSchema("enrichment", """{"type":"object","properties":{"reasons":{"type":"array"}}}""");
        var items = new[]
        {
            new BatchRequestItem("11111111-1111-1111-1111-111111111111", "system-a", "user-a", schema),
            new BatchRequestItem("22222222-2222-2222-2222-222222222222", "system-b", "user-b", schema),
        };
        return new BatchSubmission(ModelTier.Cheap, "enrich-v1", items);
    }

    [Fact]
    public async Task Submit_returns_the_provider_batch_id_and_nothing_else_is_needed_to_resume()
    {
        var handler = StubHttpMessageHandler.Always(await File.ReadAllTextAsync(Path.Combine(FixtureDir, "submit-response.json")));
        var client = NewClient(handler);

        var providerBatchId = await client.SubmitAsync(SampleSubmission(), CancellationToken.None);

        providerBatchId.ShouldBe("msgbatch_01HxYzAbCdEfGhIjKlMnOpQr");
        handler.CallCount.ShouldBe(1);
        var request = handler.Requests[0];
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri!.AbsolutePath.ShouldBe("/v1/messages/batches");
    }

    [Fact]
    public async Task Submit_builds_a_request_per_item_with_the_configured_model_and_a_forced_tool_choice()
    {
        var handler = StubHttpMessageHandler.Always(await File.ReadAllTextAsync(Path.Combine(FixtureDir, "submit-response.json")));
        var client = NewClient(handler);

        await client.SubmitAsync(SampleSubmission(), CancellationToken.None);

        var body = handler.Requests[0].Body!;
        body.ShouldContain("\"custom_id\":\"11111111-1111-1111-1111-111111111111\"");
        body.ShouldContain("\"model\":\"claude-haiku-4-5\"");
        body.ShouldContain("\"tool_choice\"");
        body.ShouldContain("\"input_schema\"");
    }

    [Fact]
    public async Task Submit_sends_the_api_key_and_version_headers()
    {
        var handler = StubHttpMessageHandler.Always(await File.ReadAllTextAsync(Path.Combine(FixtureDir, "submit-response.json")));
        var client = NewClient(handler);

        await client.SubmitAsync(SampleSubmission(), CancellationToken.None);

        var headers = handler.Requests[0].Headers;
        headers["x-api-key"].ShouldBe(ApiKey);
        headers["anthropic-version"].ShouldBe("2023-06-01");
    }

    [Fact]
    public async Task Status_maps_ended_and_the_request_counts()
    {
        var handler = StubHttpMessageHandler.Always(await File.ReadAllTextAsync(Path.Combine(FixtureDir, "status-ended.json")));
        var client = NewClient(handler);

        var status = await client.GetStatusAsync("msgbatch_01HxYzAbCdEfGhIjKlMnOpQr", CancellationToken.None);

        status.State.ShouldBe(ProviderBatchState.Ended);
        status.Succeeded.ShouldBe(2);
        status.Errored.ShouldBe(1);
    }

    [Fact]
    public async Task Status_maps_in_progress()
    {
        var handler = StubHttpMessageHandler.Always(await File.ReadAllTextAsync(Path.Combine(FixtureDir, "status-in-progress.json")));
        var client = NewClient(handler);

        var status = await client.GetStatusAsync("msgbatch_01HxYzAbCdEfGhIjKlMnOpQr", CancellationToken.None);

        status.State.ShouldBe(ProviderBatchState.InProgress);
        status.Processing.ShouldBe(3);
    }

    [Fact]
    public async Task Results_stream_item_by_item_with_raw_json_and_usage()
    {
        var handler = StubHttpMessageHandler.Always(await File.ReadAllTextAsync(Path.Combine(FixtureDir, "results.jsonl")));
        var client = NewClient(handler);

        var items = new List<BatchResultItem>();
        await foreach (var item in client.GetResultsAsync("msgbatch_01HxYzAbCdEfGhIjKlMnOpQr", CancellationToken.None))
        {
            items.Add(item);
        }

        items.Count.ShouldBe(3);
        var first = items[0];
        first.CustomId.ShouldBe("11111111-1111-1111-1111-111111111111");
        first.RawJson.ShouldNotBeNull();
        first.RawJson.ShouldContain("\"aiUsage\":\"High\"");
        first.ProviderError.ShouldBeNull();
        first.Usage.InputTokens.ShouldBe(4000);
        first.Usage.OutputTokens.ShouldBe(320);
    }

    [Fact]
    public async Task Results_surface_a_per_item_provider_error_as_a_value_not_an_exception()
    {
        var handler = StubHttpMessageHandler.Always(await File.ReadAllTextAsync(Path.Combine(FixtureDir, "results.jsonl")));
        var client = NewClient(handler);

        var items = new List<BatchResultItem>();
        await foreach (var item in client.GetResultsAsync("msgbatch_01HxYzAbCdEfGhIjKlMnOpQr", CancellationToken.None))
        {
            items.Add(item);
        }

        var errored = items.Single(i => i.CustomId == "22222222-2222-2222-2222-222222222222");
        errored.RawJson.ShouldBeNull();
        errored.ProviderError.ShouldNotBeNull();
        errored.ProviderError.ShouldContain("errored");

        // A succeeded message that carried prose instead of a tool_use block is a provider anomaly, not a crash.
        var noTool = items.Single(i => i.CustomId == "33333333-3333-3333-3333-333333333333");
        noTool.RawJson.ShouldBeNull();
        noTool.ProviderError.ShouldNotBeNull();
        noTool.ProviderError.ShouldContain("tool_use");
    }

    [Fact]
    public async Task List_recent_batches_reads_the_ids_and_created_at_and_filters_by_the_bound()
    {
        // D5 / checkpoint 4: the reconciliation read. The list is parsed into ProviderBatchRefs, an entry
        // missing created_at is skipped rather than throwing, and only batches created on or after the bound
        // (the Run's start) are returned — most recent first.
        var handler = StubHttpMessageHandler.Always(await File.ReadAllTextAsync(Path.Combine(FixtureDir, "list-batches.json")));
        var client = NewClient(handler);

        var bound = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
        var refs = await client.ListRecentBatchesAsync(bound, CancellationToken.None);

        refs.Count.ShouldBe(1);
        refs[0].ProviderBatchId.ShouldBe("msgbatch_recent_02");
        handler.Requests[0].Method.ShouldBe(HttpMethod.Get);
        handler.Requests[0].Uri!.AbsolutePath.ShouldBe("/v1/messages/batches");
    }

    [Fact]
    public async Task List_recent_batches_returns_all_recorded_batches_when_the_bound_is_open()
    {
        var handler = StubHttpMessageHandler.Always(await File.ReadAllTextAsync(Path.Combine(FixtureDir, "list-batches.json")));
        var client = NewClient(handler);

        var refs = await client.ListRecentBatchesAsync(DateTimeOffset.MinValue, CancellationToken.None);

        // The entry without a created_at is dropped; the two well-formed entries remain, most recent first.
        refs.Select(r => r.ProviderBatchId).ShouldBe(["msgbatch_recent_02", "msgbatch_recent_01"]);
    }

    [Fact]
    public async Task A_4xx_does_not_retry_and_surfaces_the_status()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":\"bad\"}") });
        var client = NewClient(handler);

        var ex = await Should.ThrowAsync<AnthropicApiException>(
            () => client.SubmitAsync(SampleSubmission(), CancellationToken.None));

        ex.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        handler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task The_api_key_never_appears_in_an_exception_message()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent(ApiKey) });
        var client = NewClient(handler);

        var ex = await Should.ThrowAsync<AnthropicApiException>(
            () => client.SubmitAsync(SampleSubmission(), CancellationToken.None));

        ex.Message.ShouldNotContain(ApiKey);
    }

    [Fact]
    public async Task Results_stream_a_realistic_result_set_without_materialising_it()
    {
        // 150 succeeded lines generated in-flight — the handler streams them and the client yields one at a time.
        const string template =
            "{\"custom_id\":\"job-ID\",\"result\":{\"type\":\"succeeded\",\"message\":{\"usage\":{\"input_tokens\":4000,\"output_tokens\":300},\"content\":[{\"type\":\"tool_use\",\"name\":\"enrichment\",\"input\":{\"reasons\":[\"r\"]}}]}}}";
        var lines = Enumerable.Range(0, 150).Select(i => template.Replace("job-ID", $"job-{i}", StringComparison.Ordinal));
        var handler = StubHttpMessageHandler.Always(string.Join("\n", lines));
        var client = NewClient(handler);

        var count = 0;
        await foreach (var item in client.GetResultsAsync("msgbatch_01HxYzAbCdEfGhIjKlMnOpQr", CancellationToken.None))
        {
            item.RawJson.ShouldNotBeNull();
            count++;
        }

        count.ShouldBe(150);
    }
}
