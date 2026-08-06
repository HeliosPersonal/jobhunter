using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using JobHunter.Domain.Preferences;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Applications;

/// <summary>
/// Turns a terminal application outcome into durable, weighted evidence for the preference learner (F6 T08,
/// SAD §6.1, AC-08). When a transition reaches <see cref="ApplicationStatus.Applied"/>,
/// <see cref="ApplicationStatus.Interview"/>, <see cref="ApplicationStatus.Offer"/> or
/// <see cref="ApplicationStatus.Rejected"/>, it reads the job's <see cref="JobFacts"/> <em>at that moment</em>
/// and stages one <see cref="Signal"/> carrying the SAD §8 weight for its kind — an interview survived a real
/// filter, a tap survived two seconds of attention, so an outcome outweighs a card action (S4).
///
/// <para>It only <em>stages</em> into the caller's unit of work through <see cref="IOutcomeSignalWriter"/>; it
/// never commits. The owner-action handler stages the signal alongside the transition and commits both in the
/// one EF transaction, so the signal and the status change are all-or-nothing (done-when 3): a signal is never
/// written for a transition that rolled back.</para>
///
/// <para>Only the four outcome kinds mint a signal — a <c>Saved</c> or <c>Ignored</c> move is F5's
/// card-action signal, and <c>New</c> is no outcome — so F6 never double-counts F5's evidence, and the
/// snapshot is not even read for a non-outcome. The facts snapshot returns only a live job; an outcome on a
/// job that has since closed stages nothing rather than fabricate a factless signal (a signal without facts
/// teaches nothing). A repeat outcome at the same instant is skipped when one is already staged
/// (<see cref="IOutcomeSignalWriter.IsStaged"/>), the in-memory belt to the database's unique
/// <c>(job_id, kind, occurred_at)</c> braces (done-when 5).</para>
///
/// <para>The weights are injected as configuration (<see cref="SignalWeights"/>, SAD §8) — never hand-copied
/// literals (done-when 4). The publisher holds no notifier or HTTP dependency, so recording an outcome can
/// only write a signal; it never acts for the Owner (invariant 7), and it selects nothing about the Owner.</para>
/// </summary>
public sealed class OutcomeSignalPublisher(
    IJobFactsSnapshotQuery facts,
    IOutcomeSignalWriter signals,
    IIdGenerator ids,
    SignalWeights weights,
    ILogger<OutcomeSignalPublisher> logger)
{
    private readonly IJobFactsSnapshotQuery _facts = facts ?? throw new ArgumentNullException(nameof(facts));
    private readonly IOutcomeSignalWriter _signals = signals ?? throw new ArgumentNullException(nameof(signals));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    private readonly SignalWeights _weights = weights ?? throw new ArgumentNullException(nameof(weights));
    private readonly ILogger<OutcomeSignalPublisher> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Stages a weighted outcome signal for the transition to <paramref name="toStatus"/> into the caller's
    /// unit of work, if that status is an outcome and the job still has facts to snapshot. Stages nothing for a
    /// non-outcome status, a closed job, or a repeat already staged this unit of work. Does not commit.
    /// </summary>
    public async Task StageAsync(
        Guid jobId,
        Guid applicationId,
        ApplicationStatus toStatus,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var kind = OutcomeKindOf(toStatus);
        if (kind is null)
        {
            // Not an outcome — a Saved/Ignored move is F5's card-action signal, New is no outcome at all. Do
            // not even read the snapshot: there is no F6 evidence to attach it to.
            return;
        }

        if (_signals.IsStaged(jobId, kind.Value, occurredAt))
        {
            // A redelivered outcome carries the same (job, kind, moment); one is already staged this unit of
            // work, so add no duplicate — the belt to the database's unique constraint (done-when 5).
            _logger.LogInformation(
                "Outcome signal {Kind} for job {JobId} at {OccurredAt:o} is already staged; skipping.",
                kind.Value, jobId, occurredAt);
            return;
        }

        var snapshot = await _facts.SnapshotAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            // The job closed or was superseded — a signal needs non-empty facts, so stage nothing rather than
            // fabricate a factless one. The transition still stands; F7 simply loses this one point.
            _logger.LogInformation(
                "Outcome {Kind} for job {JobId} has no live facts to snapshot; no signal staged.",
                kind.Value, jobId);
            return;
        }

        var signal = Signal.Capture(
            _ids.NewId(),
            jobId,
            applicationId,
            kind.Value,
            snapshot,
            occurredAt,
            _weights);

        _signals.Stage(signal);

        _logger.LogInformation(
            "Staged outcome signal {Kind} (weight {Weight}) for application {ApplicationId} on job {JobId}.",
            kind.Value, signal.Weight, applicationId, jobId);
    }

    private static SignalKind? OutcomeKindOf(ApplicationStatus status) => status switch
    {
        ApplicationStatus.Applied => SignalKind.Applied,
        ApplicationStatus.Interview => SignalKind.Interview,
        ApplicationStatus.Offer => SignalKind.Offer,
        ApplicationStatus.Rejected => SignalKind.Rejected,
        _ => null,
    };
}
