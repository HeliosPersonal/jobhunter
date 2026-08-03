using JobHunter.Domain.Companies;

namespace JobHunter.Scrapers.Detection;

/// <summary>How a detection run resolved (contract §Detection probes, AC-03/AC-04).</summary>
public enum DetectionStatus
{
    /// <summary>Exactly one candidate scored ≥ 0.80; <see cref="DetectionResult.Binding"/> is set.</summary>
    Bound,

    /// <summary>No candidate reached the discovery threshold; the company gets no binding.</summary>
    NoBoardFound,

    /// <summary>Two or more candidates scored ≥ 0.80; the company stays inactive pending a human (AC-04).</summary>
    Ambiguous,
}

/// <summary>
/// One probed candidate and the evidence that scored it (contract §Detection probes). Retained verbatim
/// in the binding's evidence document so a wrong binding is explainable without re-running detection.
/// </summary>
public sealed record ProbeCandidate(
    AtsKind Kind,
    string Token,
    decimal Score,
    int PostingsSeen,
    bool RespondedWithPostings,
    bool ApplyUrlMatchesDomain,
    bool CareersPageLinksToBoard,
    bool TokenDerivedExactly);

/// <summary>
/// The outcome of probing every provider for one company: the decision, the winning binding when there is
/// one, and the full candidate trail (both scoring and non-scoring). The trail is what makes the decision
/// auditable — including why a company was left <see cref="DetectionStatus.NoBoardFound"/>.
/// </summary>
public sealed record DetectionResult(
    DetectionStatus Status,
    AtsBinding? Binding,
    IReadOnlyList<ProbeCandidate> Candidates)
{
    /// <summary>Candidates that met the discovery threshold, highest score first.</summary>
    public IReadOnlyList<ProbeCandidate> Confident =>
        Candidates.Where(c => c.Score >= BindingConfidence.DiscoveryThreshold)
            .OrderByDescending(c => c.Score)
            .ToList();
}
