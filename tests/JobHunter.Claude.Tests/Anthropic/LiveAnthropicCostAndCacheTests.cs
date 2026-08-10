using JobHunter.Claude;
using JobHunter.Claude.Anthropic;
using JobHunter.Claude.Matching;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Profiles;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace JobHunter.Claude.Tests.Anthropic;

/// <summary>
/// The opt-in live-Anthropic cost/cache measurement (F4 T21 done-when 3, ADR-F4-0003). This is the one test
/// in the suite that talks to the real Message Batches API and spends real money, so it is gated by
/// <see cref="RequiresLiveAnthropicFactAttribute"/> — armed only when <c>ANTHROPIC_API_KEY</c> is set, and
/// excluded from the PR suite (testing-strategy §Live API tests). It runs weekly, alert-only, alongside the
/// regret sampler.
///
/// <para>Its purpose is to make the two load-bearing numbers in [[infrastructure]] §8 empirical rather than
/// asserted: the <strong>cache-hit rate</strong> (the CV prefix served from cache on every item after the
/// first, the ~47%-of-input saving the $1.03 optimised figure depends on) and the <strong>measured cost</strong>
/// of a realistic 20-item matching batch, priced through the same <see cref="CostAccountant"/> the ceiling
/// uses. The deterministic, zero-network counterpart to the cache mechanism lives in
/// <c>CvPromptCachingTests</c>; this proves the mechanism actually fires against the live provider. The CV
/// used here is a synthetic sentinel — no real CV crosses this boundary in a test.</para>
/// </summary>
public sealed class LiveAnthropicCostAndCacheTests
{
    private const int BatchSize = 20;
    private static readonly Guid ProfileId = Guid.Parse("00000000-0000-0000-0000-0000000000B1");
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    // The live cheap tier: Haiku 4.5 at list price with the 50% batch discount — the same table §8 quotes.
    private static readonly PricingOptions Pricing = new()
    {
        Tiers = new Dictionary<string, TierPricing>
        {
            ["Cheap"] = new() { ModelId = "claude-haiku-4-5", InputPerMillion = 1.00m, OutputPerMillion = 5.00m, BatchDiscount = 0.5m },
            ["Deep"] = new() { ModelId = "claude-sonnet-5", InputPerMillion = 3.00m, OutputPerMillion = 15.00m, BatchDiscount = 0.5m },
        },
    };

    private readonly ITestOutputHelper _output;

    public LiveAnthropicCostAndCacheTests(ITestOutputHelper output) => _output = output;

    [RequiresLiveAnthropicFact]
    public async Task A_matching_batch_serves_the_cv_prefix_from_cache_and_comes_in_at_or_under_the_ceiling()
    {
        // Armed only when the key is present; the attribute has already skipped otherwise, so reaching here
        // means the suite is opted in and the key is read once — never logged, never surfaced (invariant 12).
        var options = new AnthropicOptions
        {
            ApiKey = LiveAnthropicEnvironment.RequireApiKey(),
            BaseUrl = "https://api.anthropic.com",
            ApiVersion = "2023-06-01",
            MaxOutputTokens = MatchRequestBuilder.MaxOutputTokensPerItem,
        };

        using var http = new HttpClient { BaseAddress = new Uri(options.BaseUrl) };
        var client = new AnthropicBatchClient(
            http,
            Options.Create(Pricing),
            Options.Create(options),
            NullLogger<AnthropicBatchClient>.Instance);

        var request = new MatchRequestBuilder().Build(
            Enumerable.Range(0, BatchSize).Select(Job).ToList(), OwnerProfile(), Cv());
        var submission = new BatchSubmission(ModelTier.Cheap, request.PromptVersion, request.Items);

        var providerBatchId = await client.SubmitAsync(submission, CancellationToken.None);

        // A real batch can take up to the provider's 24-hour SLA; the weekly runner budgets an hour, which is
        // ample for 20 cheap-tier items. Poll on a fixed cadence until the provider reports the batch ended.
        using var budget = new CancellationTokenSource(TimeSpan.FromHours(1));
        await PollUntilEndedAsync(client, providerBatchId, budget.Token);

        var items = new List<BatchResultItem>();
        await foreach (var item in client.GetResultsAsync(providerBatchId, budget.Token))
        {
            items.Add(item);
        }

        // Every item should have succeeded — the fixtures are well-formed and the tier is live.
        items.Count.ShouldBe(BatchSize);
        items.ShouldAllBe(i => i.ProviderError == null);

        // Cache-hit rate: the CV prefix primes on the first item (cache_read 0) and is served from cache on
        // every item after it. That is the empirical form of the §8 assumption — the CV prefix, ~47% of input,
        // read at the reduced rate — so a silent cache invalidation shows up as a rate collapse, not a bill.
        var cachedItems = items.Skip(1).Count(i => i.Usage.CacheReadInputTokens > 0);
        var cacheHitRate = (double)cachedItems / (BatchSize - 1);
        _output.WriteLine($"Cache-hit rate (items after the first): {cacheHitRate:P0} ({cachedItems}/{BatchSize - 1}).");
        cacheHitRate.ShouldBeGreaterThan(0.9);

        // Measured cost: price the reported usage through the same CostAccountant the ceiling uses, and confirm
        // the batch lands at or under the pessimistic per-item ceiling the estimate would have gated. This is
        // the number §8 records — the $1.03/day optimised figure scales from a batch priced exactly this way.
        var accountant = new CostAccountant(new JobHunter.Claude.HeuristicTokenCounter(), Options.Create(Pricing));
        var totalInput = items.Sum(i => i.Usage.InputTokens);
        var totalOutput = items.Sum(i => i.Usage.OutputTokens);
        var measured = accountant.Actual(ModelTier.Cheap, totalInput, totalOutput);

        var ceiling = accountant.Estimate(
            ModelTier.Cheap,
            request.Items.Select(i => i.FullUserContent).ToList(),
            request.MaxOutputTokensPerItem);

        _output.WriteLine(
            $"Measured batch cost: ${measured.CostUsd:0.####} " +
            $"(input {totalInput}, output {totalOutput}); pessimistic ceiling ${ceiling.CostUsd:0.####}.");
        measured.CostUsd.ShouldBeLessThanOrEqualTo(ceiling.CostUsd);
        measured.CostUsd.ShouldBeGreaterThan(0m);
    }

    private static async Task PollUntilEndedAsync(
        AnthropicBatchClient client, string providerBatchId, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await client.GetStatusAsync(providerBatchId, cancellationToken);
            if (status.State == ProviderBatchState.Ended)
            {
                return;
            }

            if (status.State is ProviderBatchState.Cancelled or ProviderBatchState.Expired)
            {
                throw new InvalidOperationException($"Live batch {providerBatchId} ended in state {status.State}.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        }
    }

    private static Profile OwnerProfile() =>
        new(ProfileId, isActive: true, "Owner", 120000m, "USD", TimezoneBand.EMEA,
            ["Portugal"], [EmploymentType.FullTime], Now);

    private static CvVersion Cv() =>
        new(Guid.CreateVersion7(), ProfileId, 1, true, "cv.pdf", "application/pdf",
            2048, new string('a', 64),
            "SENTINEL_CV — fifteen years of backend engineering, Kafka, .NET, distributed systems and payments.",
            Now, Now);

    private static MatchJobContent Job(int i) =>
        new(
            Guid.Parse($"00000000-0000-0000-0000-{i:D12}"),
            $"Company {i}", "acme.com", $"Backend Engineer {i}", "Senior", "Remote — EMEA",
            "USD 120000-160000 / Year", "FullTime", $"We build payment rails number {i}. You own the ledger.",
            new MatchEnrichmentContent(
                CompanyStage.SeriesB, IsRemote: true, TimezoneBand.EMEA, IsContractorFriendly: false,
                EstimatedSalary: null, Technologies: ["C#", "Kafka"], AiUsage: AiUsageLevel.Medium));
}
