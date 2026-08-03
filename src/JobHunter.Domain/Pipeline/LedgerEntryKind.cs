namespace JobHunter.Domain.Pipeline;

/// <summary>
/// Whether a ledger entry is the estimate written <em>before</em> submission or the actual written on
/// retrieval (data-model §cost_ledger_entries, ADR-F3-0002). Keeping both kinds rather than overwriting
/// the estimate is what makes estimate accuracy measurable (NFR: within 20%). Persisted as <c>text</c>,
/// never an ordinal (coding-standards §5).
/// </summary>
public enum LedgerEntryKind
{
    /// <summary>Written before submission; the ceiling is checked against it (QG-2).</summary>
    Estimated,

    /// <summary>Written on retrieval from the provider's reported token usage.</summary>
    Actual,
}
