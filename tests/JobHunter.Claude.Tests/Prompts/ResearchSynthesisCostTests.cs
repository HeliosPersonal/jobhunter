using JobHunter.Claude;
using JobHunter.Claude.Prompts;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Research;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Prompts;

/// <summary>
/// T06: one dossier is one deep-tier item, and its cost must stay under $0.05 — asserted against the
/// pricing table, not a magic number (research-schema §Cost). A full dossier's input is dominated by the
/// fetched text (~15 000 tokens after the per-document cap), with the schema's pessimistic output ceiling;
/// priced on the Deep tier with the batch discount, that lands around $0.03, comfortably inside the ceiling.
/// A sparse document set is priced far below it, so a thin input can only ever produce a cheap, sparse
/// dossier — never a rich one padded from memory (QG-2).
/// </summary>
public sealed class ResearchSynthesisCostTests
{
    private static readonly PricingOptions Pricing = new()
    {
        Tiers = new Dictionary<string, TierPricing>
        {
            ["Cheap"] = new() { ModelId = "claude-haiku-4-5", InputPerMillion = 1.00m, OutputPerMillion = 5.00m, BatchDiscount = 0.5m },
            ["Deep"] = new() { ModelId = "claude-sonnet-5", InputPerMillion = 3.00m, OutputPerMillion = 15.00m, BatchDiscount = 0.5m },
        },
    };

    private static readonly DateTimeOffset Observed = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

    private const decimal DossierCeilingUsd = 0.05m;

    private static CostAccountant NewAccountant() => new(new HeuristicTokenCounter(), Options.Create(Pricing));

    private static string RenderFull()
    {
        // A realistic full dossier: several fetched documents each near the per-document cap, so the input
        // is dominated by fetched text — the worst realistic case for cost.
        var documents = new List<CategorisedDocument>();
        foreach (var category in new[]
                 {
                     ResearchCategory.EngineeringBlog, ResearchCategory.OpenSource,
                     ResearchCategory.Stack, ResearchCategory.InterviewProcess,
                 })
        {
            documents.Add(new CategorisedDocument(category,
                new FetchedDocument($"https://acme.ai/{category}", category.ToString(), new string('x', 15_000), Observed)));
        }

        return ResearchSynthesisPrompt.RenderUser(new ResearchPromptInput(
            DisplayName: "Acme AI",
            CanonicalDomain: "acme.ai",
            Documents: documents,
            EmptyCategories: [ResearchCategory.Funding, ResearchCategory.News, ResearchCategory.Layoffs, ResearchCategory.Reviews]));
    }

    [Fact]
    public void A_full_dossier_costs_under_the_five_cent_ceiling()
    {
        var estimate = NewAccountant().Estimate(
            ModelTier.Deep, [RenderFull()], ResearchSynthesisPrompt.MaxOutputTokens);

        estimate.CostUsd.ShouldBeLessThan(DossierCeilingUsd);
    }

    [Fact]
    public void A_sparse_document_set_is_priced_far_below_a_full_one()
    {
        var sparse = ResearchSynthesisPrompt.RenderUser(new ResearchPromptInput(
            DisplayName: "Acme AI",
            CanonicalDomain: "acme.ai",
            Documents: [new CategorisedDocument(ResearchCategory.InterviewProcess,
                new FetchedDocument("https://acme.ai/careers", "Careers", "We are hiring engineers.", Observed))],
            EmptyCategories: [ResearchCategory.Funding, ResearchCategory.News]));

        var sparseEstimate = NewAccountant().Estimate(ModelTier.Deep, [sparse], ResearchSynthesisPrompt.MaxOutputTokens);
        var fullEstimate = NewAccountant().Estimate(ModelTier.Deep, [RenderFull()], ResearchSynthesisPrompt.MaxOutputTokens);

        sparseEstimate.CostUsd.ShouldBeLessThan(fullEstimate.CostUsd);
        sparseEstimate.CostUsd.ShouldBeLessThan(DossierCeilingUsd);
    }
}
