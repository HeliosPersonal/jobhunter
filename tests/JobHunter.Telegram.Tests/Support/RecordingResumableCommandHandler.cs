using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Commands;

namespace JobHunter.Telegram.Tests.Support;

/// <summary>
/// A hand-rolled <see cref="IResumableCommandHandler"/> spy: NSubstitute cannot proxy an internal interface, so
/// the coordinator and router suites drive the resume path through this. It records the
/// <see cref="CommandRequest"/> of a normal invocation and the <see cref="CommandResumeRequest"/> of a resume,
/// so a test can assert the coordinator carried the pending state's <c>Awaiting</c>, context and the resume
/// input verbatim — and replies with fixed, caller-supplied messages so each path's output is observable.
/// </summary>
public sealed class RecordingResumableCommandHandler : IResumableCommandHandler
{
    private readonly IReadOnlyList<RenderedMessage> _handleReply;
    private readonly IReadOnlyList<RenderedMessage> _resumeReply;

    public RecordingResumableCommandHandler(string handleReply = "handled", string resumeReply = "resumed")
    {
        _handleReply = [RenderedMessage.PlainText(handleReply)];
        _resumeReply = [RenderedMessage.PlainText(resumeReply)];
    }

    public CommandRequest? Handled { get; private set; }

    public CommandResumeRequest? Resumed { get; private set; }

    public int HandleCalls { get; private set; }

    public int ResumeCalls { get; private set; }

    public Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        Handled = request;
        HandleCalls++;
        return Task.FromResult(_handleReply);
    }

    public Task<IReadOnlyList<RenderedMessage>> ResumeAsync(
        CommandResumeRequest request, CancellationToken cancellationToken = default)
    {
        Resumed = request;
        ResumeCalls++;
        return Task.FromResult(_resumeReply);
    }
}
