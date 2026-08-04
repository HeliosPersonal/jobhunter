using JobHunter.Domain.Intelligence;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// Turns one batch result item's raw tool-use JSON into a validated <see cref="Match"/> — or a recorded
/// reason it could not (match-schema §Parsing rules, T04, T06). The port lives in Domain so the
/// Application result-processing handler depends on it rather than on <c>JobHunter.Claude</c>, where the
/// tolerant parser and the wire DTO shape live; the implementation there applies the tolerant parsing
/// steps and maps the parsed output onto the domain aggregate (mirroring <see cref="IEnrichmentResultParser"/>).
///
/// <para>Parsing <strong>never throws for a single item</strong>: one malformed result is one recorded
/// failure, not a failed batch (QG-3). A successful outcome is guaranteed to carry at least one reason —
/// the tolerant parser rejects an empty <c>reasons</c> before a <see cref="Match"/> is ever constructed,
/// so invariant 4 holds by construction (AC-02). An unrecognised interview-probability band degrades to
/// <see cref="InterviewProbability.Low"/> and is noted as an anomaly rather than throwing.</para>
///
/// <para><strong>No CV text crosses this boundary.</strong> The parser sees only the model's <em>output</em>
/// (a score, a band, missing skills, a salary expectation and reasons); the CV was materialised once, in
/// the match prompt, and never travels with the result.</para>
/// </summary>
public interface IMatchResultParser
{
    /// <summary>
    /// Parses <paramref name="request"/>'s raw JSON into a <see cref="Match"/> stamped with its identity,
    /// run, profile, CV version, prompt version and creation instant, or returns a failure carrying a
    /// one-line reason and the anomalies repaired on the way. A blank payload is itself a failure.
    /// </summary>
    MatchParseOutcome Parse(MatchParseRequest request);
}

/// <summary>
/// The identity and stamping a parsed result needs to become a <see cref="Match"/> aggregate. The handler
/// owns these — the id from <see cref="IIdGenerator"/>, the instant from <see cref="IClock"/>, the profile
/// and CV version from the Run's active CV — so the parser stays pure and the result is deterministic
/// under test. <see cref="RawJson"/> is the model's output only; it carries no CV content.
/// </summary>
public sealed record MatchParseRequest(
    Guid MatchId,
    Guid JobId,
    Guid RunId,
    Guid ProfileId,
    Guid CvVersionId,
    string PromptVersion,
    DateTimeOffset CreatedAt,
    string? RawJson);

/// <summary>
/// The outcome of parsing one item: exactly one of <see cref="Match"/> (parsed and validated) or
/// <see cref="FailureReason"/> (recorded <c>ParseFailed</c>, raw retained) is present. Repairs applied on
/// the way to success — a clamped score, a swapped salary range, an interview band degraded to
/// <c>Low</c> — are listed in <see cref="Anomalies"/> so a suspiciously poor assessment stays inspectable.
/// </summary>
public sealed class MatchParseOutcome
{
    private MatchParseOutcome(Match? match, string? failureReason, IReadOnlyList<string> anomalies)
    {
        Match = match;
        FailureReason = failureReason;
        Anomalies = anomalies;
    }

    public Match? Match { get; }

    public string? FailureReason { get; }

    public bool IsSuccess => Match is not null;

    public IReadOnlyList<string> Anomalies { get; }

    public static MatchParseOutcome Success(Match match, IReadOnlyList<string>? anomalies = null)
    {
        ArgumentNullException.ThrowIfNull(match);
        return new MatchParseOutcome(match, null, anomalies ?? []);
    }

    public static MatchParseOutcome Failure(string reason) =>
        new(null, string.IsNullOrWhiteSpace(reason) ? "unspecified parse failure" : reason, []);
}
