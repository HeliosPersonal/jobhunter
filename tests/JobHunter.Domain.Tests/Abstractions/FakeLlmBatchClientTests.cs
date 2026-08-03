using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Abstractions;

public sealed class FakeLlmBatchClientTests
{
    private static readonly JsonSchema Schema = new("enrich", "{\"type\":\"object\"}");

    private static BatchSubmission Submission(int items = 3) =>
        new(
            ModelTier.Cheap,
            "enrich-v1",
            Enumerable.Range(0, items)
                .Select(i => new BatchRequestItem($"job-{i}", "system", "user", Schema))
                .ToList());

    [Fact]
    public async Task Submit_returns_the_provider_id_and_counts_the_call()
    {
        var client = new FakeLlmBatchClient { ProviderBatchId = "msgbatch_x" };

        var id = await client.SubmitAsync(Submission(), CancellationToken.None);

        id.ShouldBe("msgbatch_x");
        client.SubmitCallCount.ShouldBe(1);
        client.LastSubmission!.Items.Count.ShouldBe(3);
        client.LastSubmission.Tier.ShouldBe(ModelTier.Cheap);
    }

    [Fact]
    public async Task Throw_on_submit_mode_makes_submission_an_absence_assertion()
    {
        var client = new FakeLlmBatchClient { ThrowOnSubmit = true };

        await Should.ThrowAsync<InvalidOperationException>(
            () => client.SubmitAsync(Submission(), CancellationToken.None));
    }

    [Fact]
    public async Task Status_reports_in_progress_for_n_polls_then_ended()
    {
        var results = new[]
        {
            new BatchResultItem("job-0", "{}", null, new TokenUsage(10, 2)),
        };
        var client = new FakeLlmBatchClient(results, pollsBeforeEnd: 2);

        (await client.GetStatusAsync("id", CancellationToken.None)).State.ShouldBe(ProviderBatchState.InProgress);
        (await client.GetStatusAsync("id", CancellationToken.None)).State.ShouldBe(ProviderBatchState.InProgress);
        var ended = await client.GetStatusAsync("id", CancellationToken.None);

        ended.State.ShouldBe(ProviderBatchState.Ended);
        ended.Succeeded.ShouldBe(1);
        client.StatusCallCount.ShouldBe(3);
    }

    [Fact]
    public async Task Results_stream_every_item_and_count_the_call()
    {
        var results = new[]
        {
            new BatchResultItem("job-0", "{}", null, TokenUsage.Zero),
            new BatchResultItem("job-1", null, "overloaded", TokenUsage.Zero),
        };
        var client = new FakeLlmBatchClient(results);

        var streamed = new List<BatchResultItem>();
        await foreach (var item in client.GetResultsAsync("id", CancellationToken.None))
        {
            streamed.Add(item);
        }

        streamed.Count.ShouldBe(2);
        streamed[1].ProviderError.ShouldBe("overloaded");
        client.ResultsCallCount.ShouldBe(1);
    }

    [Fact]
    public void Jsonl_replay_maps_result_and_provider_error_lines()
    {
        var lines = new[]
        {
            "{\"custom_id\":\"job-0\",\"result\":{\"isRemote\":true},\"input_tokens\":100,\"output_tokens\":20}",
            "",
            "{\"custom_id\":\"job-1\",\"provider_error\":\"overloaded\"}",
        };

        var client = FakeLlmBatchClient.FromJsonlLines(lines);

        // Two data lines, the blank ignored.
        var results = Collect(client);
        results.Count.ShouldBe(2);
        results[0].CustomId.ShouldBe("job-0");
        results[0].RawJson.ShouldNotBeNull().ShouldContain("isRemote");
        results[0].Usage.InputTokens.ShouldBe(100);
        results[1].ProviderError.ShouldBe("overloaded");
        results[1].RawJson.ShouldBeNull();
    }

    [Fact]
    public void JsonSchema_rejects_a_blank_tool_name_or_schema()
    {
        Should.Throw<ArgumentException>(() => new JsonSchema(" ", "{}"));
        Should.Throw<ArgumentException>(() => new JsonSchema("t", " "));
    }

    private static List<BatchResultItem> Collect(FakeLlmBatchClient client)
    {
        var list = new List<BatchResultItem>();
        var enumerator = client.GetResultsAsync("id", CancellationToken.None).GetAsyncEnumerator();
        try
        {
            while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
            {
                list.Add(enumerator.Current);
            }
        }
        finally
        {
            enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        return list;
    }
}
