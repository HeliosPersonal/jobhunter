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

    [Fact]
    public void An_item_without_a_cache_prefix_writes_the_user_content_as_a_plain_string()
    {
        // Enrichment shares no per-Owner prefix, so its user content stays the simple string shape — no
        // cache_control, no content array. This is the shape the whole F3 fixture suite already asserts.
        var body = AnthropicRequestBuilder.BuildSubmitBody(
            Submission(new BatchRequestItem("job-1", "sys", "the whole prompt", Schema)), "claude-haiku-4-5", 512);

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement.GetProperty("requests")[0]
            .GetProperty("params").GetProperty("messages")[0].GetProperty("content");

        content.ValueKind.ShouldBe(JsonValueKind.String);
        content.GetString().ShouldBe("the whole prompt");
    }

    [Fact]
    public void An_item_with_a_cache_prefix_splits_the_user_message_and_marks_the_prefix_with_cache_control()
    {
        // T13 / ADR-F4-0003: the CV prefix is the cacheable head. It is written as the first content block
        // with an ephemeral cache_control breakpoint at its end; the per-item role block is the second block
        // and carries no breakpoint. This placement is what earns the 0.1× rate on every later item.
        var body = AnthropicRequestBuilder.BuildSubmitBody(
            Submission(new BatchRequestItem(
                "job-1", "sys", "ROLE BLOCK", Schema, CachePrefix: "SYSTEM+CV PREFIX")),
            "claude-sonnet-5", 550);

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement.GetProperty("requests")[0]
            .GetProperty("params").GetProperty("messages")[0].GetProperty("content");

        content.ValueKind.ShouldBe(JsonValueKind.Array);
        content.GetArrayLength().ShouldBe(2);

        var prefix = content[0];
        prefix.GetProperty("type").GetString().ShouldBe("text");
        prefix.GetProperty("text").GetString().ShouldBe("SYSTEM+CV PREFIX");
        prefix.GetProperty("cache_control").GetProperty("type").GetString().ShouldBe("ephemeral");

        var suffix = content[1];
        suffix.GetProperty("type").GetString().ShouldBe("text");
        suffix.GetProperty("text").GetString().ShouldBe("ROLE BLOCK");
        suffix.TryGetProperty("cache_control", out _).ShouldBeFalse();
    }

    [Fact]
    public void The_cache_breakpoint_falls_after_the_prefix_and_before_the_role_block()
    {
        // The falsifiable placement guarantee (T13 "done when"): everything cacheable — and nothing volatile —
        // precedes the breakpoint. If a builder ever moved a per-job value ahead of the prefix, the CV block
        // would stop being byte-identical and this ordering would break. Asserting the block index is the
        // structural form of that guarantee.
        var body = AnthropicRequestBuilder.BuildSubmitBody(
            Submission(new BatchRequestItem(
                "job-1", "sys", "ROLE", Schema, CachePrefix: "PREFIX")),
            "claude-sonnet-5", 550);

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement.GetProperty("requests")[0]
            .GetProperty("params").GetProperty("messages")[0].GetProperty("content");

        // The block carrying cache_control is the first one, and it is the prefix — not the role block.
        var breakpointIndex = -1;
        for (var i = 0; i < content.GetArrayLength(); i++)
        {
            if (content[i].TryGetProperty("cache_control", out _))
            {
                breakpointIndex = i;
            }
        }

        breakpointIndex.ShouldBe(0);
        content[breakpointIndex].GetProperty("text").GetString().ShouldBe("PREFIX");
    }
}
