namespace JobHunter.TestKit;

/// <summary>
/// Detects whether the opt-in live-Anthropic suite is armed, i.e. a real API key is present in the
/// environment. It is the live counterpart to <see cref="DockerEnvironment"/>: the F4 T21 cost/cache-hit
/// measurement talks to the real Message Batches API and spends real money, so it must never run in the PR
/// suite. It runs only when a developer (or the weekly job) explicitly sets <c>ANTHROPIC_API_KEY</c>;
/// everywhere else <see cref="RequiresLiveAnthropicFactAttribute"/> turns it into a clean skip
/// (testing-strategy §Live API tests). The key value itself is never read into a field, never logged and
/// never surfaced — only its presence is (invariant 12).
/// </summary>
public static class LiveAnthropicEnvironment
{
    /// <summary>The environment variable that arms the live suite; its value is the Anthropic API key.</summary>
    public const string ApiKeyVariable = "ANTHROPIC_API_KEY";

    /// <summary>True when a non-blank API key is present in the environment, so the live suite may run.</summary>
    public static bool IsArmed => HasUsableKey(Environment.GetEnvironmentVariable(ApiKeyVariable));

    /// <summary>
    /// The pure presence test, split out so the arming rule is one obvious place: a key is usable only when
    /// it is a non-null, non-whitespace string. Kept separate from the environment read so the rule is not
    /// entangled with process state.
    /// </summary>
    public static bool HasUsableKey(string? apiKey) => !string.IsNullOrWhiteSpace(apiKey);

    /// <summary>
    /// Returns the armed API key, or throws if the suite was run unarmed. A live test only reaches this after
    /// its <see cref="RequiresLiveAnthropicFactAttribute"/> gate has already let it run, so the throw is a
    /// guard against misuse, never an expected path.
    /// </summary>
    public static string RequireApiKey() =>
        Environment.GetEnvironmentVariable(ApiKeyVariable) is { } key && HasUsableKey(key)
            ? key
            : throw new InvalidOperationException(
                $"The live Anthropic suite requires the {ApiKeyVariable} environment variable to be set.");
}
