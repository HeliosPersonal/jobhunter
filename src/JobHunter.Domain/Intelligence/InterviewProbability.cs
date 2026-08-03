namespace JobHunter.Domain.Intelligence;

/// <summary>
/// The model's calibrated-<em>band</em> judgement of interview likelihood — deliberately a band, not a
/// percentage, until real calibration data exists (SAD §11 D4). Presenting an uncalibrated number would
/// be misleading precision; a four-value band is an honest summary. Persisted as <c>text</c>, never an
/// ordinal (coding-standards §5). <see cref="Unknown"/> is the landing place for an unrecognised or
/// absent provider value so the tolerant parser can degrade rather than throw.
/// </summary>
public enum InterviewProbability
{
    /// <summary>An interview is unlikely on the evidence.</summary>
    Low,

    /// <summary>An interview is plausible but far from assured.</summary>
    Moderate,

    /// <summary>A solid chance of an interview.</summary>
    Good,

    /// <summary>A strong chance of an interview.</summary>
    Strong,

    /// <summary>
    /// Landing place for an unrecognised or absent provider value (parsing step 8). Not part of the
    /// generated wire schema — the model is constrained to <see cref="Low"/>..<see cref="Strong"/> — but
    /// the domain enum carries it so a provider change degrades here rather than throwing at 03:00.
    /// </summary>
    Unknown,
}
