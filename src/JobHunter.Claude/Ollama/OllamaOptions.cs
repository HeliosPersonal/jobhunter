namespace JobHunter.Claude;

/// <summary>
/// Connection and model settings for the Ollama cheap-tier fallback on the helios cluster (SAD §3,
/// ADR-0005). Ollama is <em>optional</em>: its absence degrades enrichment quality, never availability,
/// so this options object is validated only when the fallback is actually selected (see the
/// <c>Llm:Provider</c> switch in <see cref="DependencyInjection"/>). It carries only what the transport
/// needs — the endpoint and the local model name — because Ollama is free, so nothing here feeds the
/// cost ceiling, which keeps pricing on the Anthropic tier (invariant 6 is enforced identically for both
/// providers, before submission).
/// </summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    /// <summary>The Ollama base address on the cluster, e.g. <c>http://ollama.helios.svc:11434</c>.</summary>
    public string BaseUrl { get; init; } = "http://localhost:11434";

    /// <summary>The local model tag Ollama serves the cheap tier from, e.g. <c>llama3.1:8b</c>.</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// The per-item output ceiling sent as <c>num_predict</c>. Deliberately generous — the enrichment
    /// schema's output is small — so it never truncates a valid assessment but still bounds a runaway.
    /// </summary>
    public int MaxOutputTokens { get; init; } = 1024;
}
