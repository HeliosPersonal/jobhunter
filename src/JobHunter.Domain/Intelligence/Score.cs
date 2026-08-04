using JobHunter.Domain.Common;

namespace JobHunter.Domain.Intelligence;

/// <summary>
/// The output of arithmetic, not of a model (data-model §scores, ADR-F4-0001): a job's place in a Run's
/// order. Its identity is the composite <c>(job_id, run_id)</c>, so it does not extend <see cref="Entity"/>.
/// Every component is stored (QG-1) and the total is <em>verified</em> against them at construction: a
/// score whose components do not reconcile to its total cannot be built (AC-03). This is the type-level
/// guarantee behind "every number is explainable".
///
/// <para>Suppression is a flag plus a reason, never a filter in a query (SAD S4): a suppressed score
/// still exists and still carries why it was hidden, so the Owner can always be told what was withheld
/// (invariant 11, AC-05). A score row may exist with <em>no</em> match — a job excluded by the pre-match
/// filter is scored, suppressed and reasoned, but never judged by the model (data-model §scores).</para>
/// </summary>
public sealed class Score
{
    /// <summary>The reconciliation tolerance between the stored total and the recomputed one (QG-1).</summary>
    private const decimal ReconcileTolerance = 0.01m;

    /// <summary>The lowest legal final score.</summary>
    public const decimal MinFinal = 0m;

    /// <summary>The highest legal final score.</summary>
    public const decimal MaxFinal = 100m;

    public Score(
        Guid jobId,
        Guid runId,
        decimal finalScore,
        ScoreComponents components,
        RankingWeights weights,
        Guid? preferenceModelId,
        bool suppressed,
        string? suppressionReason,
        DateTimeOffset computedAt)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("A Score must reference a Job.", nameof(jobId));
        }

        if (runId == Guid.Empty)
        {
            throw new ArgumentException("A Score must belong to a Run.", nameof(runId));
        }

        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(weights);

        if (finalScore is < MinFinal or > MaxFinal)
        {
            throw new ArgumentOutOfRangeException(
                nameof(finalScore),
                finalScore,
                $"A final score must be in [{MinFinal}, {MaxFinal}].");
        }

        var reconciled = components.Reconcile(weights);
        if (Math.Abs(reconciled - finalScore) > ReconcileTolerance)
        {
            // QG-1 / AC-03 as a type-level property: a score that cannot be rebuilt from its own
            // components and weights is a bug, not a rounding difference, and cannot be constructed.
            throw new ArgumentException(
                $"The final score {finalScore} does not reconcile to its components ({reconciled}).",
                nameof(finalScore));
        }

        if (suppressed)
        {
            // Invariant 11 / AC-05: a suppression without a reason is unrepresentable.
            if (string.IsNullOrWhiteSpace(suppressionReason))
            {
                throw new ArgumentException(
                    "A suppressed score must carry a suppression reason (invariant 11).",
                    nameof(suppressionReason));
            }
        }
        else if (!string.IsNullOrWhiteSpace(suppressionReason))
        {
            throw new ArgumentException(
                "A non-suppressed score must not carry a suppression reason.",
                nameof(suppressionReason));
        }

        JobId = jobId;
        RunId = runId;
        FinalScore = finalScore;
        Components = components;
        PreferenceModelId = preferenceModelId;
        Suppressed = suppressed;
        SuppressionReason = suppressed ? suppressionReason!.Trim() : null;
        ComputedAt = computedAt;
    }

    private Score()
    {
    }

    public Guid JobId { get; private set; }

    public Guid RunId { get; private set; }

    /// <summary>The 0–100 ordering key of the digest.</summary>
    public decimal FinalScore { get; private set; }

    /// <summary>The six named inputs the total reconciles from (QG-1).</summary>
    public ScoreComponents Components { get; private set; } = null!;

    /// <summary>Which preference-model version produced the preference component; null when none was active.</summary>
    public Guid? PreferenceModelId { get; private set; }

    /// <summary>True when the Owner should not be shown this job — but it is still recorded (invariant 11).</summary>
    public bool Suppressed { get; private set; }

    /// <summary>Non-null exactly when <see cref="Suppressed"/> is true (invariant 11, AC-05).</summary>
    public string? SuppressionReason { get; private set; }

    public DateTimeOffset ComputedAt { get; private set; }
}
