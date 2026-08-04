namespace JobHunter.Domain.Reporting;

/// <summary>
/// Records whether a <see cref="Digest"/>'s market note came from the model or from the template fallback
/// (data-model §digests <c>narrative_source</c>, SAD S4). It exists so that "the digest read oddly on
/// Tuesday" is answerable after the fact: a template fallback is a different artifact from a model
/// narrative and must be distinguishable. Persisted as <c>text</c>, never an ordinal (coding-standards §5).
/// </summary>
public enum NarrativeSource
{
    /// <summary>The market note was synthesised by the deep-tier model.</summary>
    Model,

    /// <summary>The model was unavailable or over budget; the deterministic template was used instead (S4).</summary>
    Template,
}
