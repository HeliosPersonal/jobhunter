namespace JobHunter.Domain.Pipeline;

/// <summary>
/// The lifecycle of a provider batch (data-model §batches <c>state</c>). Persisted as <c>text</c>,
/// never an ordinal (coding-standards §5).
/// </summary>
public enum BatchState
{
    /// <summary>Accepted by the provider; the provider batch id is persisted.</summary>
    Submitted,

    /// <summary>The provider reports it is still processing.</summary>
    InProgress,

    /// <summary>The provider reports it ended and results were retrieved.</summary>
    Completed,

    /// <summary>The batch failed provider-side, or hit the local 6 h poll cap.</summary>
    Failed,

    /// <summary>The provider expired the batch before it completed; items retry next Run.</summary>
    Expired,
}
