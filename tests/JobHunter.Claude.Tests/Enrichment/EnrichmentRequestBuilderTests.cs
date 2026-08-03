using JobHunter.Claude.Enrichment;
using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Enrichment;

/// <summary>
/// The Claude-side rendering of an enrichment batch (T10). It proves the three things the submit handler
/// relies on: the item count and prompt version are what the estimate is priced against, the custom id is
/// the job id verbatim so a result maps back with no lookup table (SAD §8), and the rendered content
/// carries the job — never the Owner — so the CV does not enter this boundary (invariant).
/// </summary>
public sealed class EnrichmentRequestBuilderTests
{
    private static EnrichmentJobContent Job(Guid id, string title = "Backend Engineer") =>
        new(id, "Acme", "acme.com", title, "Remote — EU", "EUR 80000-100000 / year", "FullTime",
            "We build payment rails. You will own the ledger service.");

    [Fact]
    public void Build_stamps_the_prompt_version_and_output_ceiling()
    {
        var request = new EnrichmentRequestBuilder().Build([Job(Guid.CreateVersion7())]);

        request.PromptVersion.ShouldBe(EnrichmentPrompt.PromptVersion);
        request.MaxOutputTokensPerItem.ShouldBe(EnrichmentRequestBuilder.MaxOutputTokensPerItem);
    }

    [Fact]
    public void Build_produces_one_item_per_job_with_the_job_id_as_custom_id()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        var request = new EnrichmentRequestBuilder().Build([Job(a), Job(b, "Platform Engineer")]);

        request.Items.Count.ShouldBe(2);
        request.Items.Select(i => i.CustomId).ShouldBe([a.ToString(), b.ToString()]);
    }

    [Fact]
    public void Build_binds_every_item_to_the_shared_system_prompt_and_schema()
    {
        var request = new EnrichmentRequestBuilder().Build([Job(Guid.CreateVersion7())]);

        var item = request.Items.ShouldHaveSingleItem();
        item.SystemPrompt.ShouldBe(EnrichmentPrompt.System);
        item.OutputSchema.ToolName.ShouldBe(EnrichmentSchema.Build().ToolName);
    }

    [Fact]
    public void Build_renders_the_job_facts_into_the_user_content()
    {
        var request = new EnrichmentRequestBuilder().Build([Job(Guid.CreateVersion7())]);

        var content = request.Items.Single().UserContent;
        content.ShouldContain("Acme");
        content.ShouldContain("Backend Engineer");
        content.ShouldContain("payment rails");
    }

    [Fact]
    public void Build_of_an_empty_scope_produces_no_items()
    {
        var request = new EnrichmentRequestBuilder().Build([]);

        request.Items.ShouldBeEmpty();
        request.PromptVersion.ShouldBe(EnrichmentPrompt.PromptVersion);
    }

    [Fact]
    public void Build_rejects_a_null_job_list()
    {
        Should.Throw<ArgumentNullException>(() => new EnrichmentRequestBuilder().Build(null!));
    }
}
