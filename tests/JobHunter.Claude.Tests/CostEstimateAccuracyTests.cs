using System.Text.Json;
using JobHunter.Claude;
using JobHunter.Domain.Pipeline;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests;

/// <summary>
/// T06: the estimate-vs-actual accuracy gate. Across the recorded corpus, the estimated input-token
/// cost must land within 20% of the provider's reported actual (contract §Cost model, NFR). This is the
/// metric that would catch a stale pricing table or a mis-calibrated token ratio (SAD §11 D3).
/// </summary>
public sealed class CostEstimateAccuracyTests
{
    private static readonly PricingOptions Pricing = new()
    {
        Tiers = new Dictionary<string, TierPricing>
        {
            ["Cheap"] = new() { ModelId = "claude-haiku-4-5", InputPerMillion = 1.00m, OutputPerMillion = 5.00m, BatchDiscount = 0.5m },
            ["Deep"] = new() { ModelId = "claude-sonnet-5", InputPerMillion = 3.00m, OutputPerMillion = 15.00m, BatchDiscount = 0.5m },
        },
    };

    [Fact]
    public void Every_corpus_entry_estimates_input_tokens_within_twenty_percent_of_the_reported_actual()
    {
        var accountant = new CostAccountant(new HeuristicTokenCounter(), Options.Create(Pricing));

        foreach (var entry in LoadCorpus())
        {
            var tier = Enum.Parse<ModelTier>(entry.Tier);
            var prompt = new string('x', entry.RenderedPromptChars);

            // Price the estimate's input tokens against the reported output actual so we isolate the
            // input-token estimation — the output side is deliberately pessimistic, not calibrated.
            var estimated = accountant.Estimate(tier, [prompt], maxOutputTokensPerItem: entry.ReportedOutputTokens);
            var actual = accountant.Actual(tier, entry.ReportedInputTokens, entry.ReportedOutputTokens);

            var drift = Math.Abs(estimated.CostUsd - actual.CostUsd) / actual.CostUsd;
            drift.ShouldBeLessThanOrEqualTo(0.20m,
                $"tier {entry.Tier}, {entry.RenderedPromptChars} chars: drift {drift:P1} exceeds 20%");
        }
    }

    private static List<CorpusEntry> LoadCorpus()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "cost", "enrichment-corpus.jsonl");
        return File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<CorpusEntry>(line, JsonOptions)!)
            .ToList();
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private sealed record CorpusEntry(
        string Tier,
        int RenderedPromptChars,
        int ReportedInputTokens,
        int ReportedOutputTokens);
}
