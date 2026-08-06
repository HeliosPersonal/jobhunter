using JobHunter.Domain.Applications;

namespace JobHunter.Domain.Preferences;

/// <summary>
/// A historical application outcome to be replayed into a <see cref="Signal"/> by the backfill (F7 T03,
/// done-when 5). It is the one durable trace of the Owner's engagement that predates the signals path: a
/// card tap left no store of its own, but every terminal outcome is an append-only
/// <c>application_transitions</c> row, so an outcome recorded before F6 began staging signals can still be
/// reconstructed. Carries only what the backfill needs — the job the outcome is about, the application it
/// belongs to, the outcome <see cref="ToStatus"/> the signal kind is derived from, and the moment it
/// occurred, which is the third part of the idempotence key.
/// </summary>
public sealed record BackfillableOutcome(
    Guid JobId,
    Guid ApplicationId,
    ApplicationStatus ToStatus,
    DateTimeOffset OccurredAt);
