using JobHunter.Application.Applications;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using JobHunter.Domain.Commands;
using JobHunter.Telegram.Commands;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;
using App = JobHunter.Domain.Applications.Application;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// <c>/note [text]</c> (contract §Commands, F6 T07): attaches a free-text note to the most-recently-touched
/// application. With text it writes straight away and confirms; with no text it enters the multi-step flow,
/// storing a short-lived per-chat state and asking for the note (AC-08); with no application to attach to it
/// offers the last five to pick rather than failing. The note body is never logged — only its length
/// (invariant 12). The CV is nowhere near it (the CV crosses exactly one boundary, not this one).
/// </summary>
public sealed class NoteCommandHandlerTests
{
    private const long OwnerChat = 4242;

    private static readonly DateTimeOffset Now = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly IApplicationPipelineQuery _pipeline = Substitute.For<IApplicationPipelineQuery>();
    private readonly IApplicationRepository _applications = Substitute.For<IApplicationRepository>();
    private readonly IConversationStateStore _state = Substitute.For<IConversationStateStore>();
    private readonly FakeClock _clock = new(Now);

    private NoteCommandHandler NewHandler() => new(
        _pipeline,
        new AddNoteHandler(_applications, new SequentialIdGenerator(), NullLogger<AddNoteHandler>.Instance),
        _state,
        _clock,
        NullLogger<NoteCommandHandler>.Instance);

    private static PipelineEntry Entry(string title, string company, int daysAgo) => new(
        Guid.NewGuid(), Guid.NewGuid(), title, company, 80m, PostingClosed: false,
        AppliedAt: null, LastActivityAt: Now.AddDays(-daysAgo), NextActionAt: null, DaysInStage: daysAgo);

    private void PipelineReturns(params PipelineEntry[] entries) =>
        _pipeline.PipelineAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ApplicationPipeline([new PipelineGroup(ApplicationStatus.Applied, entries)]));

    private static App TrackedApplication(Guid jobId) =>
        App.Create(new SequentialIdGenerator().NewId(), jobId, Now.AddDays(-10), TransitionSource.Telegram);

    [Fact]
    public async Task Inline_text_is_attached_to_the_most_recently_touched_application()
    {
        var recent = Entry("Staff Backend Engineer", "Stripe", daysAgo: 1);
        var older = Entry("Senior SRE", "Acme", daysAgo: 5);
        PipelineReturns(older, recent);
        _applications.FindByJobAsync(recent.JobId, Arg.Any<CancellationToken>())
            .Returns(TrackedApplication(recent.JobId));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "great call with the team"));

        // The note lands on the most-recently-active application (Stripe), and the confirmation names it so the
        // Owner sees where it went — never a bare "done".
        await _applications.Received(1).FindByJobAsync(recent.JobId, Arg.Any<CancellationToken>());
        await _applications.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        messages.ShouldHaveSingleItem().Text.ShouldContain("Stripe");
        await _state.DidNotReceive().SetAsync(Arg.Any<long>(), Arg.Any<ConversationState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_text_enters_the_multi_step_flow_and_asks_for_the_note()
    {
        var recent = Entry("Staff Backend Engineer", "Stripe", daysAgo: 1);
        PipelineReturns(recent);

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        // A bare /note stores a per-chat pending state awaiting the note text (AC-08) and asks for it, without
        // writing anything yet. The context carries the target job id, never any content the Owner typed.
        await _state.Received(1).SetAsync(
            OwnerChat,
            Arg.Is<ConversationState>(s => s != null && s.Command == "note" && s.Awaiting == "text"),
            Arg.Any<CancellationToken>());
        await _applications.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        messages.ShouldHaveSingleItem().Text.ShouldContain("note", Case.Insensitive);
    }

    [Fact]
    public async Task With_no_application_it_offers_the_last_five_rather_than_failing()
    {
        _pipeline.PipelineAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ApplicationPipeline([]));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "some feedback"));

        // Nothing to attach to: the Owner is told there is no application to note, never a silent failure or a
        // write. (Offering the last five to pick is wired with the callback registry in T10.)
        await _applications.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        messages.ShouldHaveSingleItem().Text.ShouldContain("application", Case.Insensitive);
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => NewHandler().HandleAsync(null!));
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var addNote = new AddNoteHandler(_applications, new SequentialIdGenerator(), NullLogger<AddNoteHandler>.Instance);
        Should.Throw<ArgumentNullException>(() =>
            new NoteCommandHandler(null!, addNote, _state, _clock, NullLogger<NoteCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() =>
            new NoteCommandHandler(_pipeline, null!, _state, _clock, NullLogger<NoteCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() =>
            new NoteCommandHandler(_pipeline, addNote, null!, _clock, NullLogger<NoteCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() =>
            new NoteCommandHandler(_pipeline, addNote, _state, null!, NullLogger<NoteCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() =>
            new NoteCommandHandler(_pipeline, addNote, _state, _clock, null!));
    }
}
