namespace JobHunter.Domain.Jobs;

/// <summary>
/// How a technology tag was matched to a job (data-model §job_technologies <c>matched_via</c>).
/// Persisted as <c>text</c>, never an ordinal (coding-standards §5). This deterministic, vocabulary-
/// matched set is kept separable from the model-extracted technologies F3 later writes elsewhere.
/// </summary>
public enum TechnologyMatch
{
    /// <summary>Matched against the job's title.</summary>
    Title,

    /// <summary>Matched against the job's description.</summary>
    Description,

    /// <summary>Matched via a vocabulary alias (e.g. "golang" → "Go").</summary>
    Vocabulary,
}
