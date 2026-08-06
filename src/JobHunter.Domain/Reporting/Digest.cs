using System.Collections.ObjectModel;
using JobHunter.Domain.Common;

namespace JobHunter.Domain.Reporting;

/// <summary>
/// One day's assembled digest — the single artifact the Owner judges the whole product by (data-model
/// §digests, SAD §1). It is built and persisted <em>before</em> any message is sent (SAD S2), so delivery
/// is a replay of stored state rather than a recomputation, and a resumed delivery shows exactly what was
/// already assembled.
///
/// <para>Two guards encode the feature's promises as type-level properties. The suppressed count must
/// reconcile to its breakdown — a footer that says "34 hidden" while its reasons sum to 30 is what makes
/// [[DECISION-LOG|D7]] a lie, and the type forbids it (invariant 11). And a model narrative must carry a
/// prompt version while a template fallback must not, so "the digest read oddly on Tuesday" is answerable
/// from the stored artifact alone (SAD S4).</para>
/// </summary>
public sealed class Digest : Entity
{
    private readonly List<DigestCard> _cards = [];
    private readonly List<SuppressionTally> _suppressionBreakdown = [];
    private readonly List<string> _degradedSources = [];

    public Digest(
        Guid id,
        Guid runId,
        DigestMode mode,
        int totalNewJobs,
        int strongMatches,
        decimal? avgSalaryUsd,
        int suppressedCount,
        IReadOnlyList<SuppressionTally> suppressionBreakdown,
        int carriedOverCount,
        int companiesChecked,
        int analysedCount,
        IReadOnlyList<string> degradedSources,
        string? narrative,
        NarrativeSource narrativeSource,
        string? promptVersion,
        IReadOnlyList<DigestCard> cards,
        DateTimeOffset generatedAt,
        int restoredCount = 0)
        : base(id)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("A Digest must belong to a Run.", nameof(runId));
        }

        ThrowIfNegative(totalNewJobs, nameof(totalNewJobs));
        ThrowIfNegative(strongMatches, nameof(strongMatches));
        ThrowIfNegative(suppressedCount, nameof(suppressedCount));
        ThrowIfNegative(carriedOverCount, nameof(carriedOverCount));
        ThrowIfNegative(companiesChecked, nameof(companiesChecked));
        ThrowIfNegative(analysedCount, nameof(analysedCount));
        ThrowIfNegative(restoredCount, nameof(restoredCount));

        if (avgSalaryUsd is <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(avgSalaryUsd),
                avgSalaryUsd,
                "The average salary is either a positive amount or absent — never zero or negative.");
        }

        ArgumentNullException.ThrowIfNull(suppressionBreakdown);
        ArgumentNullException.ThrowIfNull(degradedSources);
        ArgumentNullException.ThrowIfNull(cards);

        var breakdownSum = suppressionBreakdown.Sum(t => t.Count);
        if (breakdownSum != suppressedCount)
        {
            // Invariant 11 / D7 as a type-level property: a footer whose stated total does not match the
            // sum of its reasons is a silent-filter bug, and it cannot be constructed.
            throw new ArgumentException(
                $"The suppressed count {suppressedCount} does not reconcile to its breakdown ({breakdownSum}).",
                nameof(suppressedCount));
        }

        if (narrativeSource == NarrativeSource.Model)
        {
            if (string.IsNullOrWhiteSpace(narrative))
            {
                throw new ArgumentException(
                    "A model narrative must carry text.",
                    nameof(narrative));
            }

            if (string.IsNullOrWhiteSpace(promptVersion))
            {
                // A model call always stamps the prompt version that produced it (AC on match parity).
                throw new ArgumentException(
                    "A model narrative must carry the prompt version that produced it.",
                    nameof(promptVersion));
            }
        }
        else if (!string.IsNullOrWhiteSpace(promptVersion))
        {
            // A template fallback made no model call, so a prompt version would be a fabricated provenance.
            throw new ArgumentException(
                "A template narrative must not carry a prompt version.",
                nameof(promptVersion));
        }

        var ranks = cards.Select(c => c.Rank).ToList();
        if (ranks.Distinct().Count() != ranks.Count)
        {
            throw new ArgumentException("Digest cards must have distinct ranks.", nameof(cards));
        }

        foreach (var card in cards)
        {
            if (card.DigestId != id)
            {
                throw new ArgumentException(
                    "Every card must belong to this Digest.",
                    nameof(cards));
            }
        }

        _suppressionBreakdown = [.. suppressionBreakdown];
        _degradedSources = degradedSources
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();
        _cards = cards.OrderBy(c => c.Rank).ToList();

        RunId = runId;
        Mode = mode;
        TotalNewJobs = totalNewJobs;
        StrongMatches = strongMatches;
        AvgSalaryUsd = avgSalaryUsd;
        SuppressedCount = suppressedCount;
        CarriedOverCount = carriedOverCount;
        CompaniesChecked = companiesChecked;
        AnalysedCount = analysedCount;
        Narrative = string.IsNullOrWhiteSpace(narrative) ? null : narrative.Trim();
        NarrativeSource = narrativeSource;
        PromptVersion = string.IsNullOrWhiteSpace(promptVersion) ? null : promptVersion.Trim();
        GeneratedAt = generatedAt;
        RestoredCount = restoredCount;
    }

    private Digest()
    {
    }

    public Guid RunId { get; private set; }

    /// <summary>
    /// Which of the four header shapes this digest renders (ADR-F5-0001). Resolved once at assembly from the
    /// Run's state and counts and frozen here, so delivery — and a later re-render — reproduces the header the
    /// run earned at 06:45 rather than re-classifying a run that has since moved on (SAD §4 S2).
    /// </summary>
    public DigestMode Mode { get; private set; }

    public int TotalNewJobs { get; private set; }

    /// <summary>Count above the strong-match threshold.</summary>
    public int StrongMatches { get; private set; }

    /// <summary>Null when too few jobs carry a salary to be meaningful — better absent than misleading.</summary>
    public decimal? AvgSalaryUsd { get; private set; }

    /// <summary>Reconciles to the sum of <see cref="SuppressionBreakdown"/> by construction (AC-07, invariant 11).</summary>
    public int SuppressedCount { get; private set; }

    /// <summary>Items whose batch missed the 06:45 deadline (AC-06).</summary>
    public int CarriedOverCount { get; private set; }

    /// <summary>
    /// Active companies scanned this run — the "N companies checked, nothing matched" reassurance on a
    /// <see cref="DigestMode.NothingNew"/> day (AC-05). Zero on days where the count is not shown.
    /// </summary>
    public int CompaniesChecked { get; private set; }

    /// <summary>
    /// Scores analysed before a budget abort — the "N analysed before the daily budget was reached" line on a
    /// <see cref="DigestMode.BudgetReached"/> day (AC-06). Zero on days where the count is not shown.
    /// </summary>
    public int AnalysedCount { get; private set; }

    /// <summary>
    /// How many suppressed jobs the card floor had to restore to keep the digest from emptying (QG-3, F7 T07).
    /// Zero on a normal day. When positive, the digest states it — the least-suppressed were shown despite a
    /// learned preference, so the floor's intervention is never silent (invariant 11 in spirit: nothing shown
    /// or hidden without the Owner being told). A restored job's score row stays suppressed, so
    /// <see cref="SuppressedCount"/> and its breakdown are unchanged — restoration is a display decision, not a
    /// re-scoring.
    /// </summary>
    public int RestoredCount { get; private set; }

    /// <summary>The market note; null for an empty digest with nothing to say.</summary>
    public string? Narrative { get; private set; }

    public NarrativeSource NarrativeSource { get; private set; }

    /// <summary>Non-null exactly when <see cref="NarrativeSource"/> is <see cref="NarrativeSource.Model"/>.</summary>
    public string? PromptVersion { get; private set; }

    public DateTimeOffset GeneratedAt { get; private set; }

    /// <summary>The footer's suppression reasons and counts — what makes D7 visible (invariant 11).</summary>
    public IReadOnlyList<SuppressionTally> SuppressionBreakdown =>
        new ReadOnlyCollection<SuppressionTally>(_suppressionBreakdown);

    /// <summary>Quarantined sources named in the footer, from F1's degraded-source summary (AC-06).</summary>
    public IReadOnlyList<string> DegradedSources =>
        new ReadOnlyCollection<string>(_degradedSources);

    /// <summary>The ranked cards, ordered by <see cref="DigestCard.Rank"/>.</summary>
    public IReadOnlyList<DigestCard> Cards => new ReadOnlyCollection<DigestCard>(_cards);

    private static void ThrowIfNegative(int value, string name)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "A digest count must not be negative.");
        }
    }
}
