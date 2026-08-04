namespace JobHunter.Claude.Prompts;

/// <summary>
/// The outcome of tolerantly parsing one match result item's raw tool-use JSON (match-schema §Parsing
/// rules). Exactly one of <see cref="Output"/> (parsed and validated) or <see cref="FailureReason"/> (the
/// item is recorded <c>ParseFailed</c> with this reason, raw retained) is present. Parsing never throws
/// for a single item — one bad item is one recorded failure, not a failed batch (QG-3). Repairs applied on
/// the way to success — a clamped score, a swapped salary range, an interview band degraded to <c>Low</c> —
/// are listed in <see cref="Anomalies"/> so a suspiciously poor assessment stays inspectable.
/// </summary>
public sealed class MatchParseResult
{
    private MatchParseResult(MatchOutput? output, string? failureReason, IReadOnlyList<string> anomalies)
    {
        Output = output;
        FailureReason = failureReason;
        Anomalies = anomalies;
    }

    public MatchOutput? Output { get; }

    public string? FailureReason { get; }

    public bool IsSuccess => Output is not null;

    /// <summary>Repairs applied on the way to a successful parse (logged, not fatal).</summary>
    public IReadOnlyList<string> Anomalies { get; }

    public static MatchParseResult Success(MatchOutput output, IReadOnlyList<string>? anomalies = null) =>
        new(output, null, anomalies ?? []);

    public static MatchParseResult Failure(string reason) =>
        new(null, string.IsNullOrWhiteSpace(reason) ? "unspecified parse failure" : reason, []);
}
