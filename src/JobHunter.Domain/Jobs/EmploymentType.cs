namespace JobHunter.Domain.Jobs;

/// <summary>
/// The engagement a job offers (data-model §jobs <c>employment_type</c>). Persisted as <c>text</c>,
/// never an ordinal (coding-standards §5). <see cref="Unknown"/> is explicit rather than null, so an
/// unstated type is never mistaken for full-time.
/// </summary>
public enum EmploymentType
{
    /// <summary>Permanent, full-time employment.</summary>
    FullTime,

    /// <summary>Fixed-term or freelance contract.</summary>
    Contract,

    /// <summary>Permanent, part-time employment.</summary>
    PartTime,

    /// <summary>An internship or placement.</summary>
    Internship,

    /// <summary>The provider did not state a type.</summary>
    Unknown,
}
