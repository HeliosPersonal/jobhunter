using Xunit;

namespace JobHunter.TestKit;

/// <summary>
/// A <see cref="FactAttribute"/> that skips itself unless the live-Anthropic suite is armed (an
/// <c>ANTHROPIC_API_KEY</c> is present). It is the opt-in gate the F4 T21 cost/cache measurement rides on:
/// the test is still compiled and shipped, so it never bit-rots, but it spends real money against the real
/// Message Batches API and so must be excluded from the PR suite — a developer or CI runner without the key
/// sees a skip, never a hard failure or an unexpected bill (testing-strategy §Live API tests). Armed, it
/// runs for real, weekly and alert-only.
/// </summary>
public sealed class RequiresLiveAnthropicFactAttribute : FactAttribute
{
    public RequiresLiveAnthropicFactAttribute()
    {
        if (!LiveAnthropicEnvironment.IsArmed)
        {
            Skip = $"{LiveAnthropicEnvironment.ApiKeyVariable} is not set; opt-in live Anthropic cost/cache test skipped.";
        }
    }
}
