using JobHunter.Claude;
using JobHunter.Domain.Pipeline;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests;

/// <summary>
/// T06: <see cref="CostAccountant"/> and the pricing table. The batch discount is applied (the discounted
/// figure, not the list price, is asserted); estimates count tokens from the real rendered prompt; and
/// cost arithmetic is <c>decimal</c> throughout with no floating-point drift over a large entry count.
/// </summary>
public sealed class CostAccountantTests
{
    private static readonly PricingOptions Pricing = new()
    {
        Tiers = new Dictionary<string, TierPricing>
        {
            ["Cheap"] = new() { ModelId = "claude-haiku-4-5", InputPerMillion = 1.00m, OutputPerMillion = 5.00m, BatchDiscount = 0.5m },
            ["Deep"] = new() { ModelId = "claude-sonnet-5", InputPerMillion = 3.00m, OutputPerMillion = 15.00m, BatchDiscount = 0.5m },
        },
    };

    private static CostAccountant NewAccountant(PricingOptions? pricing = null) =>
        new(new HeuristicTokenCounter(), Options.Create(pricing ?? Pricing));

    [Fact]
    public void Estimate_prices_input_from_the_rendered_prompt_and_output_from_the_schema_ceiling()
    {
        var accountant = NewAccountant();
        // One 4000-char prompt → 1000 input tokens (÷4); output ceiling 350 tokens.
        var prompt = new string('x', 4000);

        var estimate = accountant.Estimate(ModelTier.Cheap, [prompt], maxOutputTokensPerItem: 350);

        estimate.InputTokens.ShouldBe(1000);
        estimate.OutputTokens.ShouldBe(350);
        // Discounted: (1000/1e6 * 1.00 + 350/1e6 * 5.00) * 0.5 = (0.001 + 0.00175) * 0.5 = 0.001375
        estimate.CostUsd.ShouldBe(0.001375m);
    }

    [Fact]
    public void Estimate_asserts_the_discounted_figure_not_the_list_price()
    {
        var discounted = NewAccountant();
        var listPriceOnly = new PricingOptions
        {
            Tiers = new Dictionary<string, TierPricing>
            {
                ["Cheap"] = new() { ModelId = "claude-haiku-4-5", InputPerMillion = 1.00m, OutputPerMillion = 5.00m, BatchDiscount = 0.0m },
                ["Deep"] = new() { ModelId = "claude-sonnet-5", InputPerMillion = 3.00m, OutputPerMillion = 15.00m, BatchDiscount = 0.0m },
            },
        };
        var prompt = new string('x', 4000);

        var withDiscount = discounted.Estimate(ModelTier.Cheap, [prompt], 350);
        var withoutDiscount = NewAccountant(listPriceOnly).Estimate(ModelTier.Cheap, [prompt], 350);

        // The 50% discount halves the cost — the discounted figure is what the ceiling is measured against.
        withDiscount.CostUsd.ShouldBe(withoutDiscount.CostUsd * 0.5m);
    }

    [Fact]
    public void Estimate_totals_across_a_multi_item_batch()
    {
        var accountant = NewAccountant();
        // 16000 chars → ~4000 input tokens per item, the contract's worked example at 150 jobs.
        var prompts = Enumerable.Repeat(new string('x', 16000), 150).ToList();

        var estimate = accountant.Estimate(ModelTier.Cheap, prompts, maxOutputTokensPerItem: 350);

        estimate.InputTokens.ShouldBe(150 * 4000);
        estimate.OutputTokens.ShouldBe(150 * 350);
        // The contract's ≈$0.43 worked example: 150 × (4000×1.00 + 350×5.00) / 1e6 × 0.5.
        estimate.CostUsd.ShouldBe(0.43125m);
    }

    [Fact]
    public void Actual_prices_the_providers_reported_usage_with_the_discount()
    {
        var accountant = NewAccountant();

        var actual = accountant.Actual(ModelTier.Deep, inputTokens: 6100, outputTokens: 900);

        // (6100/1e6 * 3.00 + 900/1e6 * 15.00) * 0.5 = (0.0183 + 0.0135) * 0.5 = 0.0159
        actual.CostUsd.ShouldBe(0.0159m);
        actual.InputTokens.ShouldBe(6100);
        actual.OutputTokens.ShouldBe(900);
    }

    [Fact]
    public void Cost_arithmetic_does_not_drift_over_ten_thousand_entries()
    {
        var accountant = NewAccountant();
        var single = accountant.Actual(ModelTier.Cheap, inputTokens: 4000, outputTokens: 350);

        // Summing one entry 10 000 times must equal the entry times 10 000 exactly — no float drift.
        var running = CostEstimate.Zero;
        for (var i = 0; i < 10_000; i++)
        {
            running = running.Add(single);
        }

        running.CostUsd.ShouldBe(single.CostUsd * 10_000);
        running.InputTokens.ShouldBe(4000 * 10_000);
    }

    [Fact]
    public void An_empty_batch_estimates_to_zero()
    {
        var accountant = NewAccountant();

        var estimate = accountant.Estimate(ModelTier.Cheap, [], maxOutputTokensPerItem: 350);

        estimate.ShouldBe(CostEstimate.Zero);
    }
}
