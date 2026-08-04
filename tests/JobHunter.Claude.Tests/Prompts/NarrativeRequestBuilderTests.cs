using System.Text.Json;
using JobHunter.Claude.Prompts;
using JobHunter.Domain.Reporting;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Prompts;

/// <summary>
/// T05: the Claude-side <see cref="JobHunter.Domain.Abstractions.INarrativeRequestBuilder"/> — the one place
/// the versioned narrative prompt and its generated schema are turned into a provider-agnostic
/// <see cref="JobHunter.Domain.Abstractions.BatchRequestItem"/>. A synthesis batch is always exactly one item;
/// unlike matching, there is no cache prefix; and the rendering carries only aggregate counts, so the CV
/// never enters this boundary. The output-token ceiling the estimate prices against is stated here.
/// </summary>
public sealed class NarrativeRequestBuilderTests
{
    private readonly NarrativeRequestBuilder _builder = new();

    private static NarrativeInput Sample() => new(
        TotalNewJobs: 42, StrongMatches: 6, CardCount: 5, AvgSalaryUsd: 145000m,
        SuppressedCount: 11, CarriedOverCount: 3, DegradedSourceCount: 1);

    [Fact]
    public void The_request_is_a_single_item_stamped_with_the_prompt_version()
    {
        var request = _builder.Build(Sample());

        request.PromptVersion.ShouldBe(DigestNarrativePrompt.PromptVersion);
        request.Items.Count.ShouldBe(1);
        request.MaxOutputTokensPerItem.ShouldBe(NarrativeRequestBuilder.MaxOutputTokensPerItem);
    }

    [Fact]
    public void The_item_carries_the_system_prompt_the_rendered_content_and_the_schema()
    {
        var item = _builder.Build(Sample()).Items[0];

        item.CustomId.ShouldBe(NarrativeRequestBuilder.ItemCustomId);
        item.SystemPrompt.ShouldBe(DigestNarrativePrompt.System);
        item.UserContent.ShouldBe(DigestNarrativePrompt.Render(Sample()));
        item.OutputSchema.ToolName.ShouldBe(DigestNarrativeSchema.ToolName);
    }

    [Fact]
    public void There_is_no_cache_prefix_so_the_full_user_content_is_the_rendered_content()
    {
        var item = _builder.Build(Sample()).Items[0];

        item.CachePrefix.ShouldBeNull();
        item.FullUserContent.ShouldBe(item.UserContent);
    }

    [Fact]
    public void The_schema_binds_to_a_single_required_narrative_string()
    {
        var item = _builder.Build(Sample()).Items[0];
        using var doc = JsonDocument.Parse(item.OutputSchema.SchemaJson);
        var root = doc.RootElement;

        root.GetProperty("required").EnumerateArray().Select(v => v.GetString()).ShouldBe(["narrative"]);
        var narrative = root.GetProperty("properties").GetProperty("narrative");
        narrative.GetProperty("type").GetString().ShouldBe("string");
        narrative.GetProperty("minLength").GetInt32().ShouldBe(1);
    }

    [Fact]
    public void Building_is_pure_the_same_input_yields_an_identical_item()
    {
        var a = _builder.Build(Sample()).Items[0];
        var b = _builder.Build(Sample()).Items[0];

        a.UserContent.ShouldBe(b.UserContent);
        a.SystemPrompt.ShouldBe(b.SystemPrompt);
    }
}
