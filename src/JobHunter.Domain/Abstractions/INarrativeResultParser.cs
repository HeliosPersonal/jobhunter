namespace JobHunter.Domain.Abstractions;

/// <summary>
/// Turns the market-note batch's single result item's raw tool-use JSON into the narrative text — or a
/// recorded reason it could not (F5 SAD §6.1, T05). The port lives in Domain so the Application synthesiser
/// depends on it rather than on <c>JobHunter.Claude</c>, where the tolerant parser and the wire shape live;
/// the implementation there applies the same never-throw discipline as enrichment (mirroring
/// <see cref="IEnrichmentResultParser"/>).
///
/// <para>Parsing <strong>never throws</strong>: a malformed or empty payload is a recorded failure, and the
/// synthesiser falls back to the deterministic template so the digest still ships (ADR-F5-0001). A market
/// note is not a scored item, so it carries no reasons array — invariant 4 governs Scores, Enrichments,
/// Matches and DigestCards, not the header's prose.</para>
/// </summary>
public interface INarrativeResultParser
{
    /// <summary>
    /// Parses <paramref name="rawJson"/> (the tool-use input object) into the market note. A null/blank
    /// payload, a non-object, or a missing/blank <c>narrative</c> field is a failure the caller treats as
    /// "no model note" and answers from the template instead.
    /// </summary>
    NarrativeParseOutcome Parse(string? rawJson);
}

/// <summary>
/// The outcome of parsing the market-note result: exactly one of <see cref="Narrative"/> (parsed and
/// non-blank) or <see cref="FailureReason"/> is present. Never throws for the item — a failure is a value
/// the synthesiser turns into a template fallback.
/// </summary>
public sealed class NarrativeParseOutcome
{
    private NarrativeParseOutcome(string? narrative, string? failureReason)
    {
        Narrative = narrative;
        FailureReason = failureReason;
    }

    /// <summary>The synthesised market note, trimmed and non-blank; null on failure.</summary>
    public string? Narrative { get; }

    public string? FailureReason { get; }

    public bool IsSuccess => Narrative is not null;

    public static NarrativeParseOutcome Success(string narrative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(narrative);
        return new NarrativeParseOutcome(narrative.Trim(), null);
    }

    public static NarrativeParseOutcome Failure(string reason) =>
        new(null, string.IsNullOrWhiteSpace(reason) ? "unspecified parse failure" : reason);
}
