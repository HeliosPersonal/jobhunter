using System.Text.Json;
using JobHunter.Claude;
using JobHunter.Claude.Anthropic;
using JobHunter.Claude.Matching;
using JobHunter.Claude.Prompts;
using JobHunter.Claude.Tests.Support;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Profiles;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Matching;

/// <summary>
/// T13 / ADR-F4-0003: the CV prompt cache, end to end and with zero network. The whole point of the cache
/// is load-bearing for the cost model — a silent invalidation would restore the old bill without failing
/// anything — so these tests hold the two facts CI needs: the submitted batch places one
/// <c>cache_control</c> breakpoint at the end of a byte-identical CV prefix on every item, and the parsed
/// results carry <c>cache_read_input_tokens &gt; 0</c> on every item after the first. The falsifiability
/// test proves the assertion can catch a regression: a volatile value moved ahead of the breakpoint makes
/// the shared prefix differ per item, which is exactly the state that would kill the cache — and the test
/// sees it. The real cache-hit <em>rate</em> is only observable against the live API, which is an opt-in
/// weekly test; this is its deterministic, mechanism-level counterpart (testing-strategy §Live API tests).
/// </summary>
public sealed class CvPromptCachingTests
{
    private const int BatchSize = 20;
    private static readonly Guid ProfileId = Guid.Parse("00000000-0000-0000-0000-0000000000B1");
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    private static readonly PricingOptions Pricing = new()
    {
        Tiers = new Dictionary<string, TierPricing>
        {
            ["Cheap"] = new() { ModelId = "claude-haiku-4-5", InputPerMillion = 1.00m, OutputPerMillion = 5.00m, BatchDiscount = 0.5m },
            ["Deep"] = new() { ModelId = "claude-sonnet-5", InputPerMillion = 3.00m, OutputPerMillion = 15.00m, BatchDiscount = 0.5m },
        },
    };

    private static readonly AnthropicOptions Options = new()
    {
        ApiKey = "sk-ant-secret",
        BaseUrl = "https://api.anthropic.test",
        ApiVersion = "2023-06-01",
        MaxOutputTokens = 1024,
    };

    private static Profile OwnerProfile() =>
        new(ProfileId, isActive: true, "Owner", 120000m, "USD", TimezoneBand.EMEA,
            ["Portugal"], [EmploymentType.FullTime], Now);

    private static CvVersion Cv() =>
        new(Guid.CreateVersion7(), ProfileId, 1, true, "cv.pdf", "application/pdf",
            2048, new string('a', 64), "SENTINEL_CV — fifteen years of backend engineering, Kafka, .NET.", Now, Now);

    private static MatchJobContent Job(int i) =>
        new(
            Guid.Parse($"00000000-0000-0000-0000-{i:D12}"),
            $"Company {i}", "acme.com", $"Backend Engineer {i}", "Senior", "Remote — EMEA",
            "USD 120000-160000 / Year", "FullTime", $"We build payment rails number {i}. You own the ledger.",
            new MatchEnrichmentContent(
                CompanyStage.SeriesB, IsRemote: true, TimezoneBand.EMEA, IsContractorFriendly: false,
                EstimatedSalary: null, Technologies: ["C#", "Kafka"], AiUsage: AiUsageLevel.Medium));

    private static MatchBatchRequest TwentyItemRequest() =>
        new MatchRequestBuilder().Build(
            Enumerable.Range(0, BatchSize).Select(Job).ToList(), OwnerProfile(), Cv());

    private static AnthropicBatchClient NewClient(StubHttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri(Options.BaseUrl) };
        return new AnthropicBatchClient(
            http,
            Microsoft.Extensions.Options.Options.Create(Pricing),
            Microsoft.Extensions.Options.Options.Create(Options),
            NullLogger<AnthropicBatchClient>.Instance);
    }

    [Fact]
    public void The_submitted_batch_puts_one_cache_breakpoint_on_a_byte_identical_cv_prefix_for_every_item()
    {
        var request = TwentyItemRequest();
        var body = AnthropicRequestBuilder.BuildSubmitBody(
            new BatchSubmission(ModelTier.Deep, request.PromptVersion, request.Items),
            Pricing.For(ModelTier.Deep).ModelId, Options.MaxOutputTokens);

        using var doc = JsonDocument.Parse(body);
        var requests = doc.RootElement.GetProperty("requests");
        requests.GetArrayLength().ShouldBe(BatchSize);

        string? sharedPrefix = null;
        for (var i = 0; i < BatchSize; i++)
        {
            var content = requests[i].GetProperty("params").GetProperty("messages")[0].GetProperty("content");

            // Exactly one cache_control breakpoint per item, on the first (prefix) block.
            content.ValueKind.ShouldBe(JsonValueKind.Array);
            var breakpoints = 0;
            for (var b = 0; b < content.GetArrayLength(); b++)
            {
                if (content[b].TryGetProperty("cache_control", out _))
                {
                    breakpoints++;
                }
            }

            breakpoints.ShouldBe(1);
            content[0].TryGetProperty("cache_control", out _).ShouldBeTrue();

            // The prefix carries the CV and is byte-identical across every item — the precondition for the
            // provider serving it from cache on items 2..20.
            var prefix = content[0].GetProperty("text").GetString()!;
            prefix.ShouldContain("SENTINEL_CV");
            sharedPrefix ??= prefix;
            prefix.ShouldBe(sharedPrefix);

            // The per-item role block differs and never carries the CV.
            var role = content[1].GetProperty("text").GetString()!;
            role.ShouldContain($"Backend Engineer {i}");
            role.ShouldNotContain("SENTINEL_CV");
        }
    }

    [Fact]
    public async Task Every_item_after_the_first_reports_a_cache_read_over_zero()
    {
        // The provider is simulated: the first item primes the cache (cache_read 0), every later item is
        // served the ~2400-token CV prefix from cache. This is the deterministic form of the ADR's CI
        // assertion — cache_read_input_tokens > 0 on every item after the first.
        var lines = Enumerable.Range(0, BatchSize).Select(i =>
        {
            var cacheRead = i == 0 ? 0 : 2400;
            var input = i == 0 ? 2700 : 300;
            return
                $"{{\"custom_id\":\"job-{i}\",\"result\":{{\"type\":\"succeeded\",\"message\":{{\"usage\":" +
                $"{{\"input_tokens\":{input},\"output_tokens\":300,\"cache_read_input_tokens\":{cacheRead}}}," +
                "\"content\":[{\"type\":\"tool_use\",\"name\":\"match\",\"input\":{\"reasons\":[\"r\"]}}]}}}";
        });
        var handler = StubHttpMessageHandler.Always(string.Join("\n", lines));
        var client = NewClient(handler);

        var items = new List<BatchResultItem>();
        await foreach (var item in client.GetResultsAsync("msgbatch_x", CancellationToken.None))
        {
            items.Add(item);
        }

        items.Count.ShouldBe(BatchSize);
        items[0].Usage.CacheReadInputTokens.ShouldBe(0);
        items.Skip(1).ShouldAllBe(i => i.Usage.CacheReadInputTokens > 0);
    }

    [Fact]
    public void A_volatile_value_before_the_breakpoint_makes_the_prefix_differ_per_item_which_the_assertion_catches()
    {
        // Falsifiability: were a per-job value ever placed ahead of the breakpoint, the shared prefix would
        // stop being byte-identical and the cache would silently stop hitting. We simulate exactly that — a
        // per-item id folded into the cache prefix — and prove the byte-identical assertion above would fail.
        var schema = MatchSchema.Build();
        var poisoned = Enumerable.Range(0, BatchSize).Select(i => new BatchRequestItem(
            CustomId: $"job-{i}",
            SystemPrompt: MatchPrompt.System,
            UserContent: $"role {i}",
            OutputSchema: schema,
            CachePrefix: $"CV PREFIX (run at tick {i})")); // volatile value before the breakpoint

        var body = AnthropicRequestBuilder.BuildSubmitBody(
            new BatchSubmission(ModelTier.Deep, MatchPrompt.PromptVersion, poisoned.ToList()),
            Pricing.For(ModelTier.Deep).ModelId, Options.MaxOutputTokens);

        using var doc = JsonDocument.Parse(body);
        var requests = doc.RootElement.GetProperty("requests");

        var prefixes = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < BatchSize; i++)
        {
            var content = requests[i].GetProperty("params").GetProperty("messages")[0].GetProperty("content");
            prefixes.Add(content[0].GetProperty("text").GetString()!);
        }

        // 20 distinct prefixes rather than one: the byte-identical precondition is violated, so the cache
        // would not be reused — this is the regression the identical-prefix assertion is built to catch.
        prefixes.Count.ShouldBe(BatchSize);
    }
}
