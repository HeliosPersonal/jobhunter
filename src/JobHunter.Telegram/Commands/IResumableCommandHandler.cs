using JobHunter.Domain.Notifications;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// A command that can be <em>resumed</em> — the four multi-step commands whose first step previews and stores
/// a pending <see cref="JobHunter.Domain.Commands.ConversationState"/> (<c>/note</c>, <c>/floor</c>,
/// <c>/run</c>, <c>/redeliver</c>). When the Owner's next non-command message arrives, the
/// <see cref="ConversationCoordinator"/> resolves it to a resume and hands it here through the
/// <see cref="CommandRouter"/>, so the reply completes the command rather than being treated as one of its own
/// (AC-08). The handler that <em>stored</em> the state also <em>clears</em> it when the step is terminal, so a
/// re-prompt (an unrecognised confirmation, say) can deliberately leave it pending for another reply.
///
/// <para>No resume reaches for an LLM and none touches the CV — the CV crosses exactly one boundary, and it is
/// not here. Every <see cref="ICommandHandler"/> that is resumable is also a normal handler: the inline form of
/// the command (<c>/note some text</c>) still runs through <see cref="ICommandHandler.HandleAsync"/>.</para>
/// </summary>
internal interface IResumableCommandHandler : ICommandHandler
{
    Task<IReadOnlyList<RenderedMessage>> ResumeAsync(
        CommandResumeRequest request, CancellationToken cancellationToken = default);
}
