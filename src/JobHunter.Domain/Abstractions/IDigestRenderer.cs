using JobHunter.Domain.Notifications;
using JobHunter.Domain.Reporting;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// Renders a persisted <see cref="Digest"/> into the ordered sequence of messages the delivery loop sends
/// (F5 SAD §6.1). Defined in Domain so the Application-layer delivery handler depends on the port, not on the
/// Telegram formatters: rendering needs the job facts and the inline keyboards, which live in the Telegram
/// host (the arrow runs Telegram → Application, so the handler cannot reach up to them). The handler owns the
/// idempotent send loop; the renderer owns turning stored state into <see cref="RenderableMessage"/>s.
///
/// <para>The sequence is header, then one message per card in rank order, then a footer when it has content —
/// each already carrying its <see cref="CardKey"/> so the handler can send it and record the delivery under
/// that key. The renderer reads only stored digest state and public job facts; it never touches the CV
/// (invariant: the CV crosses exactly one boundary, and it is not this one).</para>
/// </summary>
public interface IDigestRenderer
{
    /// <summary>
    /// The messages to deliver for <paramref name="digest"/>, in send order: header, cards by rank, then the
    /// footer when present. Each is keyed for idempotent delivery.
    /// </summary>
    Task<IReadOnlyList<RenderableMessage>> RenderAsync(Digest digest, CancellationToken cancellationToken = default);
}
