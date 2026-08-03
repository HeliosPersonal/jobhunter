namespace JobHunter.Domain.Pipeline;

/// <summary>
/// The pipeline stage a batch belongs to (data-model §batches <c>stage</c>). F3 submits
/// <see cref="Enrichment"/>; F4/F5/F8 reuse the same machinery with the other values. Persisted as
/// <c>text</c>, never an ordinal (coding-standards §5).
/// </summary>
public enum BatchStage
{
    /// <summary>Cheap-tier fact extraction (F3).</summary>
    Enrichment,

    /// <summary>Deep-tier CV matching (F4).</summary>
    Matching,

    /// <summary>Deep-tier company research (F8).</summary>
    Research,

    /// <summary>Deep-tier digest synthesis (F5).</summary>
    Synthesis,
}
