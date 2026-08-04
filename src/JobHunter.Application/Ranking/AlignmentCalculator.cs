using System.Globalization;
using JobHunter.Domain.Intelligence;

namespace JobHunter.Application.Ranking;

/// <summary>
/// The career-alignment component of the ranking formula (TUNE-01/F4 T14, ADR-F4-0001). A
/// <strong>static, pure</strong> function of two enrichment signals — <see cref="AiUsageLevel"/> and
/// <see cref="RoleFamily"/> — so, like <see cref="ScoreCalculator"/>, its determinism is provable rather
/// than asserted (QG-3). It answers "how well does this role fit the trajectory the Owner is deliberately
/// aiming at (AI-platform / platform / staff), independent of how well the CV already fits it?" — which is
/// the term that stops fit-to-CV burying aspiration (career-alignment review §7, §1).
///
/// <para>The result is the equal blend of two monotone maps, each in <c>[0,1]</c>: an AI-usage score
/// (None=0, Low=0.25, Medium=0.6, High=1.0) and a role-family tier score (Tier-1 target families = 1.0,
/// Tier-2 = 0.7, Tier-3 = 0.4, the anti-goal family = 0.0). Blending — rather than a min or a product —
/// means a strong signal on one axis is rewarded even when the other is weak, while the two named
/// endpoints still land exactly: a Tier-1 High-AI role is 1.0, a no-AI anti-goal role is 0.0. Every
/// result carries a reason naming both signals, so the number is accountable (invariant 4).</para>
/// </summary>
public static class AlignmentCalculator
{
    /// <summary>The weight each of the two axes carries in the blend; they sum to 1.</summary>
    private const decimal AxisWeight = 0.5m;

    /// <summary>
    /// Computes the alignment component for a role. <paramref name="aiUsage"/> and
    /// <paramref name="roleFamily"/> are the enrichment's structured signals; the result is a fraction in
    /// <c>[0,1]</c> and a reason that names both. An unrecognised AI-usage value degrades to
    /// <see cref="AiUsageLevel.None"/> rather than throwing, so a provider adding an enum value lowers a
    /// score at worst — it never crashes ranking at 03:00.
    /// </summary>
    public static AlignmentResult Calculate(AiUsageLevel aiUsage, RoleFamily roleFamily)
    {
        var aiScore = AiUsageScore(aiUsage);
        var tierScore = TierScore(roleFamily);

        var value = (AxisWeight * aiScore) + (AxisWeight * tierScore);

        var reason = string.Create(
            CultureInfo.InvariantCulture,
            $"Career alignment {value:0.00}: {roleFamily} role family (tier {tierScore:0.0}) "
            + $"with {aiUsage} AI usage ({aiScore:0.00}).");

        return new AlignmentResult(value, reason);
    }

    /// <summary>
    /// The monotone AI-usage map (None=0, Low=0.25, Medium=0.6, High=1.0). <see cref="AiUsageLevel.Unknown"/>
    /// — the tolerant parser's landing place for an unrecognised provider value — is treated as
    /// <see cref="AiUsageLevel.None"/>: absence of evidence is not evidence of alignment.
    /// </summary>
    private static decimal AiUsageScore(AiUsageLevel aiUsage) => aiUsage switch
    {
        AiUsageLevel.High => 1.00m,
        AiUsageLevel.Medium => 0.60m,
        AiUsageLevel.Low => 0.25m,
        _ => 0.00m,
    };

    /// <summary>
    /// The role-family tier map (career-alignment tuning backlog, TUNE-01/TUNE-06): the Owner's Tier-1
    /// target families score 1.0, adjacent Tier-2 engineering 0.7, Tier-3 (frontend, research-adjacent,
    /// prompt-only, and the honest "Other") 0.4, and the anti-goal enterprise-CRUD family 0.0.
    /// </summary>
    private static decimal TierScore(RoleFamily roleFamily) => roleFamily switch
    {
        RoleFamily.AiPlatform
            or RoleFamily.Platform
            or RoleFamily.AiApplications
            or RoleFamily.ForwardDeployed
            or RoleFamily.FoundingEng => 1.00m,
        RoleFamily.BackendGeneric
            or RoleFamily.Fullstack
            or RoleFamily.DevOpsSRE => 0.70m,
        RoleFamily.EnterpriseCrud => 0.00m,
        _ => 0.40m,
    };
}
