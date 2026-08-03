namespace JobHunter.Domain.Companies;

/// <summary>How a detection or re-detection run resolved (AC-03/AC-04/AC-05). The Domain-level shape of
/// the Scrapers <c>DetectionResult</c>, so the Application re-detection handler depends on a port that
/// returns Domain types rather than reaching into <c>JobHunter.Scrapers</c>.</summary>
public enum BindingDetectionStatus
{
    /// <summary>Exactly one candidate scored ≥ 0.80; <see cref="BindingDetectionResult.Binding"/> is set.</summary>
    Bound,

    /// <summary>No candidate reached the discovery threshold; the company keeps whatever binding it had.</summary>
    NoBoardFound,

    /// <summary>Two or more candidates scored ≥ 0.80; the company is left as-is pending a human (AC-04).</summary>
    Ambiguous,
}

/// <summary>
/// The Domain-level outcome of probing every provider for one company (SAD §6.2). Carries the decision
/// and, when <see cref="BindingDetectionStatus.Bound"/>, the unsaved winning <see cref="AtsBinding"/> —
/// its own id, provider, token, confidence and evidence — so the re-detection handler can compare it to
/// the company's current live binding and migrate only when the provider genuinely changed (AC-05).
/// </summary>
public sealed record BindingDetectionResult(BindingDetectionStatus Status, AtsBinding? Binding);
