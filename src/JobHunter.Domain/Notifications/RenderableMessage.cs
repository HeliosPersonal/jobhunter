using JobHunter.Domain.Reporting;

namespace JobHunter.Domain.Notifications;

/// <summary>
/// One message the delivery loop will send, paired with the <see cref="CardKey"/> that makes its delivery
/// idempotent (F5 SAD §6.1, [[adr/0002-delivery-idempotence|ADR-F5-0002]]). The renderer produces these in
/// send order — header (<see cref="CardKey.Header"/>), then a card per job (<see cref="DigestCard.Key"/>),
/// then the footer (<see cref="CardKey.Footer"/>) when it has anything to say — and the handler sends each
/// one and writes its <c>delivery_log</c> row under this key. A resumed delivery recomputes the same keys and
/// skips the ones already present, so the pairing of key to message is the whole idempotence contract.
/// </summary>
public sealed record RenderableMessage(CardKey Key, RenderedMessage Message);
