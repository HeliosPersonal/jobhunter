namespace JobHunter.Contracts.Pipeline;

/// <summary>
/// A Run's digest was assembled and fully persisted, and is ready to deliver (event-catalog §3, F5 SAD §6.1).
/// Published <em>after</em> the digest and its cards are committed (SAD S2), so a consumer that reacts to it
/// always finds the stored artifact — delivery replays state rather than recomputing it. Consumed by the
/// Telegram host, which sends the digest at 07:00 Europe/Kyiv. Carries the digest id and the number of cards
/// so a consumer need not re-query to know the shape of the day.
///
/// <para>Idempotency key is the <see cref="RunId"/>: one digest per Run is a database constraint
/// (<c>uq_digests_run</c>), so a re-assembly finds the existing digest and re-emits the same key rather than
/// producing a second artifact.</para>
/// </summary>
public sealed record DigestReady(
    Guid RunId,
    Guid DigestId,
    int CardCount,
    DateTimeOffset OccurredAt) : IIntegrationEvent;

/// <summary>
/// A card's apply destination was confirmed unreachable at digest assembly — a definitive 4xx/5xx or a
/// DNS/transport failure (F5 SAD §11 D3, AC-11). Published by the <c>ReportingHandler</c> when it drops the
/// card, so the job can be closed by the layer that owns closure rather than from the read path: F2's
/// lifecycle handler consumes it and closes the job. A timeout or a <c>robots.txt</c> refusal is
/// <em>not</em> confirmed-unreachable and never raises this — those keep the card and flag it "unverified".
///
/// <para>Idempotency key is <c>(JobId, ConfirmedAt)</c>, mirroring <c>JobClosed</c>: re-assembling the same
/// Run confirms the same job unreachable at the same instant, so a replay collapses in the inbox and the job
/// is closed once. It carries only the job and the instant — nothing about the Owner (F4 invariant).</para>
/// </summary>
public sealed record ApplyDestinationUnreachable(
    Guid JobId,
    DateTimeOffset ConfirmedAt,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
