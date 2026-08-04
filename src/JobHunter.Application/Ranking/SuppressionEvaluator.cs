using System.Globalization;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Profiles;

namespace JobHunter.Application.Ranking;

/// <summary>
/// The post-ranking suppression rules (match-schema §Suppression, invariant 11). A <strong>pure</strong>
/// evaluator, like <see cref="ScoreCalculator"/>: given a scored job, the active Profile and its salary-floor
/// opt-in, it decides whether the Owner should be shown the job — and, crucially, <em>why not</em>. Suppression
/// is never a silent filter: a suppressed job still gets a score row and always carries a reason string, so the
/// digest footer can always say what was withheld and why (invariant 11, AC-05).
///
/// <para>These are the rules for jobs that <em>were</em> matched. The two purely factual disqualifiers —
/// timezone-incompatible-and-not-remote and employment-type-not-sought — are decided <em>before</em> matching by
/// the pre-match filter (ADR-F4-0003, T12), not here. The rules applied here, in order:</para>
/// <list type="number">
/// <item><description><c>final_score &lt; 40</c> → <c>Below presentation threshold</c>. A job preferences pushed
/// below the bar the digest will show.</description></item>
/// <item><description>Salary estimate below the Owner's floor, at high confidence, <em>and</em> the Owner has
/// opted the floor in as a hard rule → <c>Below salary floor ({amount})</c>. Off by default: the floor is a
/// down-weight, not a filter, until explicitly opted in (O5).</description></item>
/// </list>
/// </summary>
public static class SuppressionEvaluator
{
    /// <summary>The lowest final score the digest will show; below it a job is suppressed (match-schema §Suppression).</summary>
    public const decimal PresentationThreshold = 40m;

    /// <summary>The salary-estimate confidence at or above which the floor rule may bite, when opted in.</summary>
    public const decimal HighConfidence = 0.7m;

    /// <summary>
    /// Decides suppression for one scored job. <paramref name="estimatedSalary"/> is the enrichment's estimated
    /// pay (or null), used only by the floor rule. <paramref name="salaryFloorOptIn"/> is the Owner's explicit
    /// opt-in that turns the salary floor from a down-weight into a hard suppression rule (O5); it is false by
    /// default. Returns the reason string when the job is suppressed, or <c>null</c> when it should be shown.
    /// </summary>
    public static string? Evaluate(
        ScoreResult score, SalaryEstimate? estimatedSalary, Profile profile, bool salaryFloorOptIn)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (score.FinalScore < PresentationThreshold)
        {
            return "Below presentation threshold";
        }

        if (salaryFloorOptIn && BelowSalaryFloor(estimatedSalary, profile, out var floorReason))
        {
            return floorReason;
        }

        return null;
    }

    private static bool BelowSalaryFloor(SalaryEstimate? estimatedSalary, Profile profile, out string reason)
    {
        reason = string.Empty;

        if (profile.SalaryFloor is not { } floor || profile.SalaryFloorCurrency is not { } floorCurrency)
        {
            return false;
        }

        if (estimatedSalary is not { } estimate)
        {
            return false;
        }

        // Never compare across currencies — a euros-versus-dollars answer would be a lie (SalaryEstimate). If
        // the estimate is in a different currency we cannot judge the floor, so we do not suppress on it.
        if (!string.Equals(estimate.Currency, floorCurrency, StringComparison.Ordinal))
        {
            return false;
        }

        if (estimate.Confidence < HighConfidence)
        {
            return false;
        }

        // The whole estimated range sits below the floor: even the top of the band misses it.
        if (estimate.Max >= floor)
        {
            return false;
        }

        reason = string.Create(
            CultureInfo.InvariantCulture, $"Below salary floor ({floorCurrency} {floor:0.##})");
        return true;
    }
}
