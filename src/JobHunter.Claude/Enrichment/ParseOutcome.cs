namespace JobHunter.Claude.Enrichment;

/// <summary>
/// The outcome of parsing one batch result item (enrichment-schema §Parsing rules). Exactly one of
/// <see cref="Output"/> (the item parsed and validated) or <see cref="FailureReason"/> (the item is
/// recorded <c>ParseFailed</c> with this reason, raw retained) is present. Parsing never throws for a
/// single item — one bad item is one recorded failure, not a failed batch (QG-3). Anomalies that were
/// repaired rather than rejected (a swapped salary range, a clamped confidence, a degraded enum) are
/// listed in <see cref="Anomalies"/> so a suspiciously poor assessment is inspectable.
/// </summary>
public sealed class ParseOutcome
{
    private ParseOutcome(EnrichmentOutput? output, string? failureReason, IReadOnlyList<string> anomalies)
    {
        Output = output;
        FailureReason = failureReason;
        Anomalies = anomalies;
    }

    public EnrichmentOutput? Output { get; }

    public string? FailureReason { get; }

    public bool IsSuccess => Output is not null;

    /// <summary>Repairs applied on the way to a successful parse (logged, not fatal).</summary>
    public IReadOnlyList<string> Anomalies { get; }

    public static ParseOutcome Success(EnrichmentOutput output, IReadOnlyList<string>? anomalies = null) =>
        new(output, null, anomalies ?? []);

    public static ParseOutcome Failure(string reason) =>
        new(null, string.IsNullOrWhiteSpace(reason) ? "unspecified parse failure" : reason, []);
}
