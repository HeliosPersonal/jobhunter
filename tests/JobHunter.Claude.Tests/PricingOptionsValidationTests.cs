using JobHunter.Claude;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests;

/// <summary>
/// T06: the pricing table's startup validation (ADR-F3-0002, SAD §11 D3). A missing tier, or both tiers
/// pointing at one model id, must fail the host at startup rather than mispricing silently — an unpriced
/// or doubled tier would make the cost ceiling meaningless. Validation is triggered here by resolving the
/// options, exactly as <c>ValidateOnStart</c> does at host build.
/// </summary>
public sealed class PricingOptionsValidationTests
{
    private static PricingOptions Resolve(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var provider = new ServiceCollection()
            .AddJobHunterClaude(configuration)
            .BuildServiceProvider();
        return provider.GetRequiredService<IOptions<PricingOptions>>().Value;
    }

    private static Dictionary<string, string?> ValidTable() => new()
    {
        ["Pricing:Tiers:Cheap:ModelId"] = "claude-haiku-4-5",
        ["Pricing:Tiers:Cheap:InputPerMillion"] = "1.00",
        ["Pricing:Tiers:Cheap:OutputPerMillion"] = "5.00",
        ["Pricing:Tiers:Cheap:BatchDiscount"] = "0.5",
        ["Pricing:Tiers:Deep:ModelId"] = "claude-sonnet-5",
        ["Pricing:Tiers:Deep:InputPerMillion"] = "3.00",
        ["Pricing:Tiers:Deep:OutputPerMillion"] = "15.00",
        ["Pricing:Tiers:Deep:BatchDiscount"] = "0.5",
    };

    [Fact]
    public void A_complete_pricing_table_validates()
    {
        var pricing = Resolve(ValidTable());

        pricing.For(Domain.Pipeline.ModelTier.Cheap).ModelId.ShouldBe("claude-haiku-4-5");
        pricing.For(Domain.Pipeline.ModelTier.Deep).OutputPerMillion.ShouldBe(15.00m);
    }

    [Fact]
    public void A_missing_tier_fails_startup()
    {
        var settings = ValidTable();
        settings.Remove("Pricing:Tiers:Deep:ModelId");
        settings.Remove("Pricing:Tiers:Deep:InputPerMillion");
        settings.Remove("Pricing:Tiers:Deep:OutputPerMillion");
        settings.Remove("Pricing:Tiers:Deep:BatchDiscount");

        var ex = Should.Throw<OptionsValidationException>(() => Resolve(settings));
        ex.Message.ShouldContain("every ModelTier");
    }

    [Fact]
    public void Two_tiers_sharing_one_model_id_fails_startup()
    {
        var settings = ValidTable();
        settings["Pricing:Tiers:Deep:ModelId"] = "claude-haiku-4-5";

        var ex = Should.Throw<OptionsValidationException>(() => Resolve(settings));
        ex.Message.ShouldContain("same model id");
    }

    [Fact]
    public void A_non_positive_rate_fails_startup()
    {
        var settings = ValidTable();
        settings["Pricing:Tiers:Cheap:InputPerMillion"] = "0";

        var ex = Should.Throw<OptionsValidationException>(() => Resolve(settings));
        ex.Message.ShouldContain("positive");
    }

    [Fact]
    public void A_discount_of_one_or_more_fails_startup()
    {
        var settings = ValidTable();
        settings["Pricing:Tiers:Cheap:BatchDiscount"] = "1.0";

        Should.Throw<OptionsValidationException>(() => Resolve(settings));
    }
}
