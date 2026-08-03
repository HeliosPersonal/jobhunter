using JobHunter.Domain.Pipeline;

namespace JobHunter.Application.Enrichment;

/// <summary>
/// The 02:00 tick that opens the day's Run (F3 SAD §6.1). Enqueued by Hangfire and handled by
/// <see cref="RunOrchestrator"/>, which creates the Run, snapshots the ceiling, and selects the scope.
/// An internal application message, not a cross-boundary integration event, so it lives in the
/// Application layer rather than in <c>Contracts</c>. <see cref="WindowEnd"/> is the discovery window's
/// <c>cutoff_to</c>, stamped once when the tick fires.
/// </summary>
public sealed record StartDailyRun(DateTimeOffset WindowEnd);

/// <summary>
/// A Run has a non-empty scope and is ready for the enrichment batch to be built, estimated, gated on
/// the cost ceiling and submitted (F3 SAD §6.2, T10). Published by <see cref="RunOrchestrator"/> for a
/// freshly-started or resumed <see cref="RunState.Created"/> Run; handled by the submission step. Keyed
/// on <see cref="RunId"/> so a redelivery submits at most once (the unique <c>(run_id, stage, tier)</c>
/// index is the hard guarantee).
/// </summary>
public sealed record EnrichmentSubmissionDue(Guid RunId);

/// <summary>
/// A Run is in <see cref="RunState.Enriching"/> with a submitted batch whose results are not yet
/// retrieved, so its provider batch should be polled (F3 SAD §6.2, T11). Published by
/// <see cref="RunOrchestrator"/> when resuming an <c>Enriching</c> Run — the batch is polled, never
/// resubmitted (AC-05). Keyed on <see cref="RunId"/>.
/// </summary>
public sealed record BatchPollDue(Guid RunId);

/// <summary>
/// One non-terminal Run must be re-entered at its current state after a restart (F3 SAD §6.1, QG-1,
/// AC-05). Published one-per-Run by the startup resume sweep so a single Run's resumption is a single
/// message's concern; handled by <see cref="RunOrchestrator"/>, which loads the Run and dispatches on
/// its state. Keyed on <see cref="RunId"/> — resuming the same Run twice converges rather than
/// duplicating, because every downstream step is itself idempotent.
/// </summary>
public sealed record ResumeRun(Guid RunId);
