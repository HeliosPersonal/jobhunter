using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Anthropic;

/// <summary>
/// The arming rule for the opt-in live-Anthropic suite (F4 T21). These are the deterministic, zero-network
/// counterpart to the live test itself: they pin the one decision that keeps a real, money-spending batch
/// out of the PR suite — a blank or absent key never arms it, a real key does — so the gate cannot silently
/// invert and start (or stop) billing. They assert the pure <see cref="LiveAnthropicEnvironment.HasUsableKey"/>
/// predicate rather than the process environment, so they run the same everywhere.
/// </summary>
public sealed class LiveAnthropicArmingTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_or_blank_key_does_not_arm_the_live_suite(string? apiKey)
    {
        LiveAnthropicEnvironment.HasUsableKey(apiKey).ShouldBeFalse();
    }

    [Fact]
    public void A_present_key_arms_the_live_suite()
    {
        LiveAnthropicEnvironment.HasUsableKey("sk-ant-a-real-looking-key").ShouldBeTrue();
    }
}
