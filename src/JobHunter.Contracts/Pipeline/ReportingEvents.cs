namespace JobHunter.Contracts.Pipeline;

/// <summary>
/// A Run's digest was assembled and fully persisted (event-catalog §3, F5 SAD §6.1). Published <em>after</em>
/// the digest and its cards are committed (SAD S2). This is an assembled-and-persisted marker with no
/// consumer: the digest is built and held earlier in the day, and delivery is a separate slot — the Worker's
/// <c>DigestDeliveryTrigger</c> fires <c>DigestDeliveryDue</c> at 07:00 Europe/Kyiv and the Worker's
/// <c>DeliveryHandler</c> sends by replaying the stored artifact. Carries the digest id and the number of
/// cards so any future consumer need not re-query to know the shape of the day.
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
/// A Run's digest was delivered to the Owner — every card that could be sent has been sent and logged
/// (event-catalog §3, F5 SAD §6.1). Published <em>after</em> the delivery loop completes, so a consumer
/// reacting to it knows the morning digest is out. Carries the counts the loop actually achieved: how many
/// messages were sent this pass and how many cards were refused (a per-card 400), so the number is a fact
/// about delivery, not a restatement of the digest's shape.
///
/// <para>Idempotency key is <c>(RunId, ChatId)</c>: a repeated <c>DigestDeliveryDue</c> tick re-runs a delivery
/// that finds every card already in the log and sends nothing, then re-emits this with the same key. It carries
/// only counts and the chat — nothing about the Owner beyond the chat id (F4 invariant).</para>
/// </summary>
public sealed record DigestDelivered(
    Guid RunId,
    long ChatId,
    int MessagesSent,
    int CardsFailed,
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
