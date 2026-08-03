namespace JobHunter.Claude;

/// <summary>
/// Connection and request settings for the Anthropic Message Batches API, bound from configuration and
/// validated at startup (coding-standards §options). The model id per tier lives in
/// <see cref="PricingOptions"/> — the single place a model upgrade is a configuration change — so this
/// options object carries only what pricing does not: the endpoint, the API version headers and the
/// per-item output ceiling. The API key is bound here but <strong>never logged, never put in a span and
/// never placed in an exception message</strong> (invariant 12).
/// </summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    /// <summary>The API key, injected from Infisical at runtime. Secret — it crosses no log boundary.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>The API base address; defaults to the public endpoint.</summary>
    public string BaseUrl { get; init; } = "https://api.anthropic.com";

    /// <summary>The <c>anthropic-version</c> header value.</summary>
    public string ApiVersion { get; init; } = "2023-06-01";

    /// <summary>
    /// The per-item <c>max_tokens</c> ceiling sent on every request. Deliberately pessimistic — the
    /// enrichment schema's output is small, so a generous cap costs nothing in the estimate but stops a
    /// runaway generation.
    /// </summary>
    public int MaxOutputTokens { get; init; } = 1024;
}
