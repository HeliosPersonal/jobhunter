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
