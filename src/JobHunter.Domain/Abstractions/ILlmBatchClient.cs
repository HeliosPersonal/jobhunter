using JobHunter.Domain.Pipeline;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The provider-agnostic port for the whole asynchronous batch lifecycle and nothing else (SAD §5, S6):
/// submit a batch, poll its status, stream its results. All Anthropic specifics live in the adapter, so
/// the pipeline is tested fixture-driven with zero network and Ollama becomes a second adapter rather
/// than a fork. No provider concept — no HTTP, no message-batch id format, no tool-use envelope — leaks
/// across this boundary.
/// </summary>
public interface ILlmBatchClient
{
    /// <summary>
    /// Submits <paramref name="submission"/> and returns the provider's batch id. That id is the single
    /// fact that makes resumption possible (S2), so the caller persists it immediately. Submission is the
    /// only spend-committing call; the cost ceiling is enforced <em>before</em> it (QG-2).
    /// </summary>
    Task<string> SubmitAsync(BatchSubmission submission, CancellationToken cancellationToken);

    /// <summary>Reports the current provider-side status of the batch named by <paramref name="providerBatchId"/>.</summary>
    Task<BatchStatus> GetStatusAsync(string providerBatchId, CancellationToken cancellationToken);

    /// <summary>
    /// Streams the batch's results one item at a time (SAD §5). Streaming keeps a 150-item result set out
    /// of memory and makes per-item failure isolation natural (QG-3): each item carries its own raw JSON
    /// or provider error, so one bad item is one recorded failure rather than a failed batch.
    /// </summary>
    IAsyncEnumerable<BatchResultItem> GetResultsAsync(string providerBatchId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the provider's batches created on or after <paramref name="createdOnOrAfter"/>, most recent
    /// first. This is the reconciliation read that closes the one window in the whole feature where money
    /// could be spent without a record (SAD §11 D5, crash-matrix checkpoint 4): the provider id lives only
    /// in the persisted batch row, and there is a one-statement gap between <see cref="SubmitAsync"/>
    /// returning that id and the row committing. A restart in that gap must <em>adopt</em> the batch the
    /// provider already holds rather than submit — and pay — a second time. A single active Run submits
    /// exactly one enrichment batch, so at most one batch created since the Run began can be its own, which
    /// is what makes adoption unambiguous.
    /// </summary>
    Task<IReadOnlyList<ProviderBatchRef>> ListRecentBatchesAsync(
        DateTimeOffset createdOnOrAfter,
        CancellationToken cancellationToken);
}

/// <summary>
/// A provider batch seen from the outside during reconciliation: its id and the instant the provider
/// created it. Deliberately minimal — it carries only what adoption needs (SAD §11 D5). The item-level
/// detail is reached later through <see cref="ILlmBatchClient.GetResultsAsync"/>.
/// </summary>
public sealed record ProviderBatchRef(string ProviderBatchId, DateTimeOffset CreatedAt);

/// <summary>One batch to submit: the tier, the prompt version stamped on every row, and the items.</summary>
public sealed record BatchSubmission(
    ModelTier Tier,
    string PromptVersion,
    IReadOnlyList<BatchRequestItem> Items);

/// <summary>
/// One item's request. <see cref="CustomId"/> is the job id verbatim, so a result maps back with no
/// lookup table (SAD §8). The output is schema-bound via <see cref="OutputSchema"/> (ADR-0006).
/// </summary>
public sealed record BatchRequestItem(
    string CustomId,
    string SystemPrompt,
    string UserContent,
    JsonSchema OutputSchema);

/// <summary>
/// One item's result as returned by the provider: exactly one of <see cref="RawJson"/> (the tool-use
/// payload to parse) or <see cref="ProviderError"/> (the provider failed this item) is present. The
/// <see cref="Usage"/> is per-batch token accounting reported alongside.
/// </summary>
public sealed record BatchResultItem(
    string CustomId,
    string? RawJson,
    string? ProviderError,
    TokenUsage Usage);

/// <summary>
/// The provider-side status of a batch, mapped to a small provider-agnostic vocabulary. Counts let the
/// poller log progress; <see cref="ProviderBatchState.Ended"/> is the only state that triggers
/// retrieval.
/// </summary>
public sealed record BatchStatus(
    ProviderBatchState State,
    int Succeeded,
    int Errored,
    int Processing);

/// <summary>Input/output token counts reported by the provider, used to write the actual cost ledger entry.</summary>
public sealed record TokenUsage(int InputTokens, int OutputTokens)
{
    public static readonly TokenUsage Zero = new(0, 0);
}

/// <summary>
/// The provider-agnostic batch state the adapter maps every provider's own vocabulary onto. Keeping it
/// separate from <see cref="BatchState"/> (the persisted lifecycle) means a provider adding a status
/// value is an adapter change, not a schema change.
/// </summary>
public enum ProviderBatchState
{
    /// <summary>The provider is still processing the batch.</summary>
    InProgress,

    /// <summary>The batch finished and results can be retrieved.</summary>
    Ended,

    /// <summary>The batch was cancelled provider-side.</summary>
    Cancelled,

    /// <summary>The provider expired the batch before it completed.</summary>
    Expired,
}

/// <summary>
/// A JSON Schema for tool-use structured output, held as its raw JSON text plus the tool name it binds
/// to. A domain type — not a library schema object — so <c>JobHunter.Domain</c> keeps referencing
/// nothing external (T03). The adapter turns it into the provider's tool-use envelope.
/// </summary>
public sealed record JsonSchema
{
    public JsonSchema(string toolName, string schemaJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaJson);
        ToolName = toolName;
        SchemaJson = schemaJson;
    }

    /// <summary>The tool-use function name the schema binds to.</summary>
    public string ToolName { get; }

    /// <summary>The JSON Schema document as raw text.</summary>
    public string SchemaJson { get; }
}
