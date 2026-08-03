using System.Text.Json;
using JobHunter.Claude.Anthropic;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Anthropic;

/// <summary>
/// T07: <see cref="AnthropicRequestBuilder"/> is a pure function of its inputs (SAD §5). Asserting the
/// serialised body directly is what lets the whole adapter be tested with zero network.
/// </summary>
public sealed class AnthropicRequestBuilderTests
{
    private static readonly JsonSchema Schema = new("enrichment", """{"type":"object","required":["reasons"]}""");

    private static BatchSubmission Submission(params BatchRequestItem[] items) =>
        new(ModelTier.Cheap, "enrich-v1", items);

    [Fact]
    public void Body_carries_one_request_per_item_with_the_model_and_max_tokens()
    {
        var submission = Submission(
            new BatchRequestItem("job-1", "sys", "content-1", Schema),
            new BatchRequestItem("job-2", "sys", "content-2", Schema));

        var body = AnthropicRequestBuilder.BuildSubmitBody(submission, "claude-haiku-4-5", 1024);

        using var doc = JsonDocument.Parse(body);
        var requests = doc.RootElement.GetProperty("requests");
        requests.GetArrayLength().ShouldBe(2);

        var first = requests[0];
        first.GetProperty("custom_id").GetString().ShouldBe("job-1");
        var @params = first.GetProperty("params");
        @params.GetProperty("model").GetString().ShouldBe("claude-haiku-4-5");
        @params.GetProperty("max_tokens").GetInt32().ShouldBe(1024);
    }

    [Fact]
    public void Body_binds_structured_output_with_a_forced_tool_choice()
    {
        var body = AnthropicRequestBuilder.BuildSubmitBody(
            Submission(new BatchRequestItem("job-1", "sys", "content", Schema)), "claude-haiku-4-5", 512);

        using var doc = JsonDocument.Parse(body);
        var @params = doc.RootElement.GetProperty("requests")[0].GetProperty("params");

        var tool = @params.GetProperty("tools")[0];
        tool.GetProperty("name").GetString().ShouldBe("enrichment");
        tool.GetProperty("input_schema").GetProperty("type").GetString().ShouldBe("object");

        var choice = @params.GetProperty("tool_choice");
        choice.GetProperty("type").GetString().ShouldBe("tool");
        choice.GetProperty("name").GetString().ShouldBe("enrichment");
    }

    [Fact]
    public void An_empty_batch_produces_an_empty_requests_array()
    {
        var body = AnthropicRequestBuilder.BuildSubmitBody(Submission(), "claude-haiku-4-5", 1024);

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("requests").GetArrayLength().ShouldBe(0);
    }
}
