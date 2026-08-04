using JobHunter.Domain.Intelligence;

namespace JobHunter.Application.Ranking;

/// <summary>
/// The transparent linear ranking function (ADR-F4-0001, SAD §8, T07). A <strong>static, pure</strong>
/// function of explicit values — no clock, no repository, no options object, no ambient culture — which is
/// precisely what makes its determinism provable rather than asserted (QG-3). Given the model's fit
/// judgement, the career-alignment component, an optional preference component, whether an enrichment is
/// present, and the timestamps, it produces the 0–100 ordering key and the named components that rebuild
/// it (QG-1):
///
/// <code>final = 100 × (w_m·match + w_a·alignment + w_p·preference + w_f·freshness) × confidence</code>
///
/// <para>When no preference model is active the preference weight is redistributed across the remaining
/// weights in proportion, so the effective weights still sum to 1 and the preference term contributes
/// nothing — a job is never penalised for the absence of a model that has not been trained yet. Freshness
/// decays as <c>exp(-ageDays/7)</c>, capped at 1.00 for a just-seen (or future-dated) posting and
/// approaching 0 for an ancient one. Confidence is a multiplier — 1.00 with an enrichment, 0.85 without —
/// so uncertainty lowers a score rather than excluding the job (AC-09).</para>
/// </summary>
public static class ScoreCalculator
{
    /// <summary>The freshness half-life constant: a job a week old scores ~0.37 on this component (SAD §8).</summary>
    private const double FreshnessDecayDays = 7.0;

    /// <summary>The confidence multiplier applied when no enrichment backs the job (AC-09).</summary>
    private const decimal ConfidenceWithoutEnrichment = 0.85m;

    /// <summary>
    /// Scores one job. <paramref name="alignment"/> is the career-alignment component in <c>[0,1]</c>
    /// (from <see cref="AlignmentCalculator"/>). <paramref name="preference"/> is null when no preference
    /// model is active, in which case its weight is renormalised away rather than counted as zero fit.
    /// Every argument is a value: the same arguments always produce the same result, on any thread, in
    /// any culture, in any order.
    /// </summary>
    public static ScoreResult Calculate(
        MatchFacts match,
        decimal alignment,
        decimal? preference,
        bool hasEnrichment,
        DateTimeOffset firstSeenAt,
        DateTimeOffset now,
        RankingWeights weights)
    {
        ArgumentNullException.ThrowIfNull(weights);

        var matchComponent = match.MatchScore / 100m;
        var freshnessComponent = Freshness(firstSeenAt, now);
        var confidence = hasEnrichment ? 1.00m : ConfidenceWithoutEnrichment;

        var preferencePresent = preference is not null;
        var preferenceComponent = preference ?? 0m;
        var effectiveWeights = preferencePresent ? weights : Renormalise(weights);

        var components = new ScoreComponents(
            matchComponent, alignment, preferenceComponent, freshnessComponent, confidence);

        // Derive the total from the very components and weights that are stored, so reconciliation is exact
        // by construction (QG-1): there is no second, drifting copy of the arithmetic.
        var finalScore = Clamp(components.Reconcile(effectiveWeights), Score.MinFinal, Score.MaxFinal);

        return new ScoreResult(match.JobId, finalScore, components, effectiveWeights, preferencePresent);
    }

    /// <summary>
    /// Orders scored jobs the way the digest shows them: highest final score first, ties broken by ascending
    /// job id so the order is total and reproducible (T07 done-when, QG-3). A pure projection of its input.
    /// </summary>
    public static IEnumerable<ScoreResult> Rank(IEnumerable<ScoreResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        return results
            .OrderByDescending(r => r.FinalScore)
            .ThenBy(r => r.JobId);
    }

    private static decimal Freshness(DateTimeOffset firstSeenAt, DateTimeOffset now)
    {
        var ageDays = (now - firstSeenAt).TotalDays;
        if (ageDays <= 0)
        {
            // A just-seen or (from clock skew) future-dated posting is at full freshness, never above it.
            return 1.00m;
        }

        var decayed = Math.Exp(-ageDays / FreshnessDecayDays);
        return Clamp((decimal)decayed, 0m, 1m);
    }

    private static RankingWeights Renormalise(RankingWeights weights)
    {
        // Redistribute the preference weight across match, alignment and freshness in proportion to their
        // shares, so the effective weights still sum to 1. If none remains (a degenerate all-preference
        // config), there is nothing to renormalise onto — keep the weights; the zero preference component
        // means the preference term contributes nothing regardless.
        var remaining = weights.Match + weights.Alignment + weights.Freshness;
        if (remaining <= 0m)
        {
            return weights;
        }

        return new RankingWeights(
            match: weights.Match / remaining,
            alignment: weights.Alignment / remaining,
            preference: 0m,
            freshness: weights.Freshness / remaining);
    }

    private static decimal Clamp(decimal value, decimal min, decimal max) =>
        value < min ? min : value > max ? max : value;
}
