using JobHunter.Domain.Notifications;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// One command's behaviour (T11). A handler turns a <see cref="CommandRequest"/> into the messages to send
/// back to the Owner — one for most commands, several for a command like <c>/digest</c> that replays a whole
/// digest. It returns the messages rather than sending them itself, so the dispatch, the sending and the
/// rendering stay separable and every handler is unit-testable against the returned value with no notifier.
///
/// <para>No handler reaches for an LLM: the command path is deterministic (ADR-F10-0002), and the CV crosses
/// exactly one boundary, which is not here.</para>
/// </summary>
internal interface ICommandHandler
{
    Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default);
}
