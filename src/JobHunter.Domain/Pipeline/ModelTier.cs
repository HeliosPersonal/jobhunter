namespace JobHunter.Domain.Pipeline;

/// <summary>
/// The two-tier cascade (ADR-0005): <see cref="Cheap"/> extracts facts (F3 enrichment), <see cref="Deep"/>
/// makes judgements (F4 matching, F5 synthesis, F8 research). The tier maps to a configured model id in
/// <c>PricingTable</c>; a model upgrade is a configuration change, not a code change. Persisted as
/// <c>text</c>, never an ordinal (coding-standards §5).
/// </summary>
public enum ModelTier
{
    /// <summary>The cheap extraction tier — Haiku by default.</summary>
    Cheap,

    /// <summary>The deep judgement tier — Sonnet by default.</summary>
    Deep,
}
