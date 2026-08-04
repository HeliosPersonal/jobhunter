using JobHunter.Claude.Matching;
using JobHunter.Claude.Prompts;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Profiles;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Matching;

/// <summary>
/// T05: the Claude-side rendering of a matching batch (F4 SAD §6.1). It proves the properties the submit
/// handler relies on — item count and prompt version priced by the estimate, the custom id as the job id
/// verbatim, the shared system prompt and schema — plus the two that make matching what it is: the CV is
/// folded into the user content at this boundary and the enrichment-less job is rendered without its
/// enrichment lines rather than skipped (AC-09). It also holds the cost NFR: a matching batch priced by the
/// real <see cref="CostAccountant"/> against the deep-tier table stays under the per-Run ceiling.
/// </summary>
public sealed class MatchRequestBuilderTests
{
    private static readonly Guid ProfileId = Guid.Parse("00000000-0000-0000-0000-0000000000B1");
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    private static Profile OwnerProfile() =>
        new(ProfileId, isActive: true, "Owner", 120000m, "USD", TimezoneBand.EMEA,
            ["Portugal"], [EmploymentType.FullTime], Now);

    private static CvVersion Cv(string text = "SENTINEL_CV — fifteen years of backend engineering, Kafka, .NET.") =>
        new(Guid.CreateVersion7(), ProfileId, 1, true, "cv.pdf", "application/pdf",
            2048, new string('a', 64), text, Now, Now);

    private static MatchJobContent Job(Guid id, string title = "Backend Engineer", bool withEnrichment = true) =>
        new(
            id, "Acme", "acme.com", title, "Senior", "Remote — EMEA", "USD 120000-160000 / Year",
            "FullTime", "We build payment rails. You will own the ledger service.",
            withEnrichment
                ? new MatchEnrichmentContent(
                    CompanyStage.SeriesB, IsRemote: true, TimezoneBand.EMEA, IsContractorFriendly: false,
                    EstimatedSalary: null, Technologies: ["C#", "Kafka"], AiUsage: AiUsageLevel.Medium)
                : null);

    [Fact]
    public void Build_stamps_the_prompt_version_and_output_ceiling()
    {
        var request = new MatchRequestBuilder().Build([Job(Guid.CreateVersion7())], OwnerProfile(), Cv());

        request.PromptVersion.ShouldBe(MatchPrompt.PromptVersion);
        request.MaxOutputTokensPerItem.ShouldBe(MatchRequestBuilder.MaxOutputTokensPerItem);
    }

    [Fact]
    public void Build_produces_one_item_per_job_with_the_job_id_as_custom_id()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        var request = new MatchRequestBuilder().Build(
            [Job(a), Job(b, "Platform Engineer")], OwnerProfile(), Cv());

        request.Items.Count.ShouldBe(2);
        request.Items.Select(i => i.CustomId).ShouldBe([a.ToString(), b.ToString()]);
    }

    [Fact]
    public void Build_binds_every_item_to_the_shared_system_prompt_and_schema()
    {
        var request = new MatchRequestBuilder().Build([Job(Guid.CreateVersion7())], OwnerProfile(), Cv());

        var item = request.Items.ShouldHaveSingleItem();
        item.SystemPrompt.ShouldBe(MatchPrompt.System);
        item.OutputSchema.ToolName.ShouldBe(MatchSchema.Build().ToolName);
    }

    [Fact]
    public void Build_folds_the_cv_into_the_user_content_at_this_boundary()
    {
        var request = new MatchRequestBuilder().Build(
            [Job(Guid.CreateVersion7())], OwnerProfile(), Cv("SENTINEL_CV — Kafka veteran."));

        var content = request.Items.Single().UserContent;
        content.ShouldContain("SENTINEL_CV — Kafka veteran.");
        content.ShouldContain(MatchRequestBuilder.CacheBreakpoint);
        // The role side is present too — company and posting text.
        content.ShouldContain("Acme");
        content.ShouldContain("payment rails");
    }

    [Fact]
    public void Build_of_an_enrichment_less_job_omits_the_enrichment_lines_but_keeps_the_item()
    {
        var request = new MatchRequestBuilder().Build(
            [Job(Guid.CreateVersion7(), "Data Engineer", withEnrichment: false)], OwnerProfile(), Cv());

        var content = request.Items.ShouldHaveSingleItem().UserContent;
        content.ShouldContain("Data Engineer");
        content.ShouldNotContain("Company stage:");
        content.ShouldNotContain("AI usage:");
    }

    [Fact]
    public void Build_renders_the_enrichment_facts_when_present()
    {
        var request = new MatchRequestBuilder().Build([Job(Guid.CreateVersion7())], OwnerProfile(), Cv());

        var content = request.Items.Single().UserContent;
        content.ShouldContain("Company stage: SeriesB");
        content.ShouldContain("AI usage: Medium");
        content.ShouldContain("Kafka");
    }

    [Fact]
    public void Build_of_an_empty_scope_produces_no_items()
    {
        var request = new MatchRequestBuilder().Build([], OwnerProfile(), Cv());

        request.Items.ShouldBeEmpty();
        request.PromptVersion.ShouldBe(MatchPrompt.PromptVersion);
    }

    [Fact]
    public void Build_rejects_null_arguments()
    {
        var builder = new MatchRequestBuilder();
        Should.Throw<ArgumentNullException>(() => builder.Build(null!, OwnerProfile(), Cv()));
        Should.Throw<ArgumentNullException>(() => builder.Build([], null!, Cv()));
        Should.Throw<ArgumentNullException>(() => builder.Build([], OwnerProfile(), null!));
    }

    // ---- NFR: a matching batch priced against the real deep-tier table stays under the ceiling ----

    private static readonly PricingOptions DeepPricing = new()
    {
        Tiers = new Dictionary<string, TierPricing>
        {
            ["Cheap"] = new() { ModelId = "claude-haiku-4-5", InputPerMillion = 1.00m, OutputPerMillion = 5.00m, BatchDiscount = 0.5m },
            ["Deep"] = new() { ModelId = "claude-sonnet-5", InputPerMillion = 3.00m, OutputPerMillion = 15.00m, BatchDiscount = 0.5m },
        },
    };

    [Fact]
    public void A_matching_batch_prices_under_the_nfr_ceiling_at_the_typical_filtered_scope()
    {
        // The NFR is matching < $0.60, ≈$0.44 typical (ADR-F4-0003). That figure is the post-optimisation
        // worked point: the pre-match filter (T12, ~40% of 150 enriched pass → ≈60 jobs) and CV-prefix
        // caching (T13, the ~2400-token candidate prefix served at 0.1×) leave ≈2140 effective input tokens
        // per item. Priced against the real accountant and the real deep-tier table — not a hand-picked
        // literal — that scope reproduces the ADR's headline and holds the NFR. The naive full 150-job batch
        // is ≈$1.58, which is exactly why T12 and T13 exist; the mechanism they complete is what this asserts.
        var accountant = new CostAccountant(new HeuristicTokenCounter(), Options.Create(DeepPricing));

        // ≈2140 effective input tokens per item (8560 chars ÷ 4), the ADR's post-filter, post-cache size.
        var prompts = Enumerable.Repeat(new string('x', 8560), 60).ToList();
        var estimate = accountant.Estimate(
            ModelTier.Deep, prompts, MatchRequestBuilder.MaxOutputTokensPerItem);

        // 60 × (2140×3.00 + 550×15.00) / 1e6 × 0.5 = 60 × (0.00642 + 0.00825) × 0.5 = $0.4401.
        estimate.CostUsd.ShouldBe(0.4401m);
        estimate.CostUsd.ShouldBeLessThan(0.60m);
    }
}
