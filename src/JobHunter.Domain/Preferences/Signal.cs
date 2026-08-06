using JobHunter.Domain.Common;

namespace JobHunter.Domain.Preferences;

/// <summary>
/// One recorded reaction of the Owner to a job — the atomic evidence the preference learner fits on
/// (F7 [[data-model]] §signals). Written by F5 (card actions) and F6 (application outcomes); F7 owns the
/// schema and is the only reader. It carries the <see cref="Kind"/> of reaction, its evidence
/// <see cref="Weight"/> (resolved from <see cref="SignalWeights"/> per kind, SAD §8), and a
/// <see cref="JobFacts"/> snapshot of the job as it was <em>when reacted to</em>.
///
/// <para>The construction guards are the point of the type. A signal must reference a job, and its facts
/// snapshot must be non-empty — a signal without facts teaches nothing (T01 AC). An outcome signal
/// (<see cref="SignalKind.Applied"/>, <see cref="SignalKind.Interview"/>, <see cref="SignalKind.Offer"/>,
/// <see cref="SignalKind.Rejected"/>) must reference the application it came from; a card action has no
/// application. Idempotent capture lives at the database — unique <c>(job_id, kind, occurred_at)</c>, so a
/// redelivered action produces no second signal — not here.</para>
/// </summary>
public sealed class Signal : Entity
{
    public Signal(
        Guid id,
        Guid jobId,
        Guid? applicationId,
        SignalKind kind,
        decimal weight,
        JobFacts jobFacts,
        DateTimeOffset occurredAt)
        : base(id)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("A Signal must reference a Job.", nameof(jobId));
        }

        ArgumentNullException.ThrowIfNull(jobFacts);

        if (weight <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(weight), weight, "A signal weight must be strictly positive.");
        }

        if (IsOutcome(kind))
        {
            if (applicationId is null || applicationId == Guid.Empty)
            {
                throw new ArgumentException(
                    $"An outcome signal ({kind}) must reference the application it came from.",
                    nameof(applicationId));
            }
        }
        else if (applicationId is not null)
        {
            throw new ArgumentException(
                $"A card-action signal ({kind}) does not belong to an application.",
                nameof(applicationId));
        }

        JobId = jobId;
        ApplicationId = applicationId;
        Kind = kind;
        Weight = weight;
        JobFacts = jobFacts;
        OccurredAt = occurredAt;
    }

    private Signal()
    {
    }

    /// <summary>
    /// Captures a signal with its evidence weight resolved from <paramref name="weights"/> for the given
    /// <paramref name="kind"/>, so callers never hand-copy the SAD §8 table. This is how F5 and F6 mint a
    /// signal; the raw constructor exists for materialisation and for a caller that already has the weight.
    /// </summary>
    public static Signal Capture(
        Guid id,
        Guid jobId,
        Guid? applicationId,
        SignalKind kind,
        JobFacts jobFacts,
        DateTimeOffset occurredAt,
        SignalWeights weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        return new Signal(id, jobId, applicationId, kind, weights.WeightFor(kind), jobFacts, occurredAt);
    }

    public Guid JobId { get; private set; }

    /// <summary>Non-null exactly for the outcome kinds; the application the outcome belongs to (F6).</summary>
    public Guid? ApplicationId { get; private set; }

    public SignalKind Kind { get; private set; }

    /// <summary>The evidence weight this reaction contributes (SAD §8), strictly positive.</summary>
    public decimal Weight { get; private set; }

    /// <summary>The job's facts when the Owner reacted — non-empty by construction.</summary>
    public JobFacts JobFacts { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }

    private static bool IsOutcome(SignalKind kind) =>
        kind is SignalKind.Applied or SignalKind.Interview or SignalKind.Offer or SignalKind.Rejected;
}
