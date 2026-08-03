namespace JobHunter.Domain.Pipeline;

/// <summary>
/// The per-item outcome of parsing a batch result (data-model §batch_items <c>state</c>). One row per
/// item is what makes failure isolation possible: a bad item is one <see cref="ParseFailed"/> row, not a
/// failed Run (QG-3). Persisted as <c>text</c>, never an ordinal (coding-standards §5).
/// </summary>
public enum BatchItemState
{
    /// <summary>Submitted, awaiting a result.</summary>
    Pending,

    /// <summary>Parsed and validated into an enrichment.</summary>
    Parsed,

    /// <summary>The result was malformed, schema-invalid or reasonless; raw retained for 30 days.</summary>
    ParseFailed,

    /// <summary>The provider reported an error for this specific item; retry once next Run.</summary>
    ProviderError,

    /// <summary>Failed twice across two Runs; dropped and never retried again (AC-08).</summary>
    Abandoned,
}
