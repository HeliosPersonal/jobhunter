using System.Globalization;
using JobHunter.Domain.Preferences;

namespace JobHunter.Application.Ranking;

/// <summary>
/// Applies the Owner's <see cref="SuppressionOverride"/> rules on top of the model's suppression verdict (F7
/// data-model §suppression_overrides, AC-06). An override is a <em>stated</em> rule — it needs no supporting
/// evidence, only a <c>(dimension, value)</c> and a direction — and it outranks whatever the learner inferred.
/// A <strong>pure</strong> function, like <see cref="SuppressionEvaluator"/>: given the model's reason (or null
/// when it would show the job), the job's <see cref="JobFacts"/> and the active overrides, it returns the final
/// verdict and any <em>tension</em> — a one-line record of where an override contradicted the model, so the
/// override is never a silent rewrite (invariant 11).
///
/// <para>Precedence, most-decisive first:</para>
/// <list type="number">
/// <item><description>A matching <see cref="SuppressionMode.AlwaysSuppress"/> hides the job with its own reason,
/// naming the deliberate rule rather than the generic threshold. It wins even over a contradictory
/// <see cref="SuppressionMode.NeverSuppress"/> on the same job: of two conflicting Owner rules, hiding what the
/// Owner told us to hide is the safer resolution.</description></item>
/// <item><description>A matching <see cref="SuppressionMode.NeverSuppress"/> vetoes a model suppression, forcing
/// the job to appear (AC-06).</description></item>
/// <item><description>Otherwise the model's verdict stands unchanged.</description></item>
/// </list>
/// A tension is recorded whenever an override reversed the model — an always-suppress over a job the model would
/// have shown, a never-suppress over a job the model suppressed, or the two rules colliding — and left null when
/// override and model agree.
/// </summary>
public static class SuppressionOverrideResolver
{
    /// <summary>
    /// Resolves one job's final suppression verdict against the Owner's overrides. <paramref name="modelReason"/>
    /// is the pure <see cref="SuppressionEvaluator"/>'s verdict (a reason, or null to show). <paramref name="facts"/>
    /// is the job's characteristics in the <see cref="Dimension"/> vocabulary; <paramref name="overrides"/> are the
    /// active rules. Returns the final reason (null to show) and any tension the override introduced.
    /// </summary>
    public static OverrideResolution Resolve(
        string? modelReason,
        JobFacts facts,
        IReadOnlyList<SuppressionOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(overrides);

        SuppressionOverride? alwaysSuppress = null;
        SuppressionOverride? neverSuppress = null;

        foreach (var rule in overrides)
        {
            if (!Matches(rule, facts))
            {
                continue;
            }

            if (rule.Mode == SuppressionMode.AlwaysSuppress)
            {
                alwaysSuppress ??= rule;
            }
            else
            {
                neverSuppress ??= rule;
            }
        }

        // AlwaysSuppress is the most decisive: it hides the job with its own reason and wins even over a
        // contradictory NeverSuppress. A tension is noted when it reversed the model (would-show) or collided
        // with a NeverSuppress; when the model already suppressed and no NeverSuppress contradicts, they agree.
        if (alwaysSuppress is not null)
        {
            var reason = ReasonFor(alwaysSuppress);
            var tension = modelReason is null
                ? TensionFor(alwaysSuppress, "the model would have shown it")
                : neverSuppress is not null
                    ? TensionFor(alwaysSuppress, "a never-suppress rule contradicts it")
                    : null;
            return new OverrideResolution(reason, tension);
        }

        // A NeverSuppress vetoes a model suppression, recording the tension. When the model would have shown the
        // job anyway, the override merely agrees and no tension is recorded.
        if (neverSuppress is not null && modelReason is not null)
        {
            return new OverrideResolution(
                null, TensionFor(neverSuppress, $"the model suppressed it ({modelReason})"));
        }

        return new OverrideResolution(modelReason, null);
    }

    private static bool Matches(SuppressionOverride rule, JobFacts facts) =>
        facts.ValuesFor(rule.Dimension)
            .Any(v => string.Equals(v, rule.Value, StringComparison.OrdinalIgnoreCase));

    private static string ReasonFor(SuppressionOverride rule) =>
        string.Create(CultureInfo.InvariantCulture, $"Owner rule: always hide {rule.Dimension} {rule.Value}");

    private static string TensionFor(SuppressionOverride rule, string against) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Owner {rule.Mode} override on {rule.Dimension} {rule.Value} applied although {against}.");
}

/// <summary>
/// The outcome of applying overrides to one job (F7 T07). <see cref="Reason"/> is the final suppression reason —
/// null when the job is shown — and <see cref="Tension"/> records where an override contradicted the model, or is
/// null when they agreed. The tension is the non-silent trail the handler logs and counts, so an override never
/// quietly rewrites a decision (invariant 11).
/// </summary>
public readonly record struct OverrideResolution(string? Reason, string? Tension);
