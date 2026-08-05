namespace JobHunter.Application.Delivery;

/// <summary>
/// The 07:00 Europe/Kyiv tick that delivers the day's digest to the Owner (ADR-F5-0001 — "07:00 is a hard
/// commitment", SAD §6.3). Enqueued by Hangfire and handled by <see cref="DeliveryHandler"/>: it resolves
/// the day's Run, loads the digest assembled at 06:45, and runs the idempotent send loop. Delivery is a
/// scheduled slot rather than a reaction to <c>DigestReady</c>, so nothing reaches the Owner before 07:00 —
/// a digest assembled early sits until the slot opens.
///
/// <para>An internal application message, not a cross-boundary integration event, so it lives in the
/// Application layer rather than <c>Contracts</c>. <see cref="DueAt"/> is stamped once when the tick fires.
/// The per-card idempotence that makes a redelivered tick safe is the <c>delivery_log</c> unique constraint
/// (ADR-F5-0002), not this message.</para>
/// </summary>
public sealed record DigestDeliveryDue(DateTimeOffset DueAt);
