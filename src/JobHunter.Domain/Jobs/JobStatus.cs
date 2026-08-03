namespace JobHunter.Domain.Jobs;

/// <summary>
/// The lifecycle state of a canonical job (data-model §jobs <c>status</c>). Persisted as <c>text</c>,
/// never an ordinal (coding-standards §5). Transitions are governed by the <c>Job</c> aggregate, which
/// refuses the illegal ones (T01, T08).
/// </summary>
public enum JobStatus
{
    /// <summary>The opening is believed open: at least one alias has been seen recently.</summary>
    Live,

    /// <summary>Every alias has gone stale, or a provider reported the posting gone.</summary>
    Closed,

    /// <summary>Withheld from the pipeline pending review — never auto-closed or auto-reopened.</summary>
    Quarantined,
}
