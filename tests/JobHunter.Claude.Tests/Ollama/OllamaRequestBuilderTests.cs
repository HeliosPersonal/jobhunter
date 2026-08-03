using System.Text.Json;
using JobHunter.Claude.Ollama;
using JobHunter.Domain.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Ollama;

/// <summary>
/// T14: <see cref="OllamaRequestBuilder"/> is a pure function of its inputs (SAD §3), so the fallback
/// adapter's request shape is asserted directly with zero network. The point of these assertions is that
/// the structured-output binding carries the <em>same</em> JSON Schema the Anthropic tier binds, so both
/// providers emit the identical wire shape the one tolerant parser reads (ADR-0006).
/// </summary>
public sealed class OllamaRequestBuilderTests
{
    private static readonly JsonSchema Schema = new("enrichment", """{"type":"object","required":["reasons"]}""");

    [Fact]
    public void Body_carries_the_model_the_turn_and_num_predict()
    {
        var item = new BatchRequestItem("job-1", "system-prompt", "the rendered job", Schema);

        var body = OllamaRequestBuilder.BuildChatBody(item, "llama3.1:8b", 512);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.GetProperty("model").GetString().ShouldBe("llama3.1:8b");
        root.GetProperty("stream").GetBoolean().ShouldBeFalse();
        root.GetProperty("options").GetProperty("num_predict").GetInt32().ShouldBe(512);

        var messages = root.GetProperty("messages");
        messages.GetArrayLength().ShouldBe(2);
        messages[0].GetProperty("role").GetString().ShouldBe("system");
        messages[0].GetProperty("content").GetString().ShouldBe("system-prompt");
        messages[1].GetProperty("role").GetString().ShouldBe("user");
        messages[1].GetProperty("content").GetString().ShouldBe("the rendered job");
    }

    [Fact]
    public void Body_binds_structured_output_with_the_same_schema_the_anthropic_tier_uses()
    {
        var item = new BatchRequestItem("job-1", "sys", "content", Schema);

        var body = OllamaRequestBuilder.BuildChatBody(item, "llama3.1:8b", 1024);

        using var doc = JsonDocument.Parse(body);
        var format = doc.RootElement.GetProperty("format");
        format.GetProperty("type").GetString().ShouldBe("object");
        format.GetProperty("required")[0].GetString().ShouldBe("reasons");
    }

    [Fact]
    public void An_empty_system_prompt_is_omitted_leaving_only_the_user_turn()
    {
        var item = new BatchRequestItem("job-1", string.Empty, "content", Schema);

        var body = OllamaRequestBuilder.BuildChatBody(item, "llama3.1:8b", 1024);

        using var doc = JsonDocument.Parse(body);
        var messages = doc.RootElement.GetProperty("messages");
        messages.GetArrayLength().ShouldBe(1);
        messages[0].GetProperty("role").GetString().ShouldBe("user");
    }
}
