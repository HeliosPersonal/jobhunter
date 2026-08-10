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
/// The confirmation arms of <c>/note</c> (F6 T07): each <see cref="AddNoteOutcome"/> renders a distinct,
/// application-named reply, never a bare "done" and never the note body (invariant 12). The refusals — a body
/// over the length cap, and a job no application tracks anymore — are covered for both the inline write and
/// the resumed reply, along with the resumed reply whose target has since vanished from the pipeline (named
/// "your latest application") and the empty-body resume that falls through to the "nothing to note" line.
/// </summary>
public sealed class NoteCommandHandlerBranchTests
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
    public async Task An_over_long_inline_note_is_refused_by_naming_the_length_cap_never_the_body()
    {
        var recent = Entry("Staff Backend Engineer", "Stripe", daysAgo: 1);
        PipelineReturns(recent);
        var overLong = new string('x', ApplicationNote.MaxLength + 1);

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, overLong));

        // A body over the cap is a refusal, not a write; the reply cites the cap and never echoes the body.
        await _applications.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("too long", Case.Insensitive);
        text.ShouldContain(ApplicationNote.MaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
        text.ShouldNotContain("xxxx");
    }

    [Fact]
    public async Task An_inline_note_for_an_untracked_application_says_none_tracks_it_anymore()
    {
        var recent = Entry("Staff Backend Engineer", "Stripe", daysAgo: 1);
        PipelineReturns(recent);
        // The pipeline still lists it, but the write path finds no application to attach to.
        _applications.FindByJobAsync(recent.JobId, Arg.Any<CancellationToken>()).Returns((App?)null);

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "quick thought"));

        await _applications.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("Stripe");
        text.ShouldContain("anymore", Case.Insensitive);
    }

    [Fact]
    public async Task A_resumed_reply_whose_target_has_vanished_confirms_against_your_latest_application()
    {
        // The pipeline no longer lists the stored job, but the write still succeeds keyed off the job id; the
        // confirmation then falls back to a generic "your latest application" rather than a stale name.
        var jobId = Guid.NewGuid();
        PipelineReturns(Entry("Unrelated Role", "Acme", daysAgo: 2));
        _applications.FindByJobAsync(jobId, Arg.Any<CancellationToken>()).Returns(TrackedApplication(jobId));
        var resume = new CommandResumeRequest(
            OwnerChat, "text",
            new Dictionary<string, string> { ["jobId"] = jobId.ToString() },
            "chase them next week");

        var messages = await NewHandler().ResumeAsync(resume);

        await _applications.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        messages.ShouldHaveSingleItem().Text.ShouldContain("latest application", Case.Insensitive);
    }

    [Fact]
    public async Task A_resumed_over_long_reply_is_refused_by_naming_the_length_cap()
    {
        var recent = Entry("Staff Backend Engineer", "Stripe", daysAgo: 1);
        PipelineReturns(recent);
        _applications.FindByJobAsync(recent.JobId, Arg.Any<CancellationToken>())
            .Returns(TrackedApplication(recent.JobId));
        var overLong = new string('y', ApplicationNote.MaxLength + 1);
        var resume = new CommandResumeRequest(
            OwnerChat, "text",
            new Dictionary<string, string> { ["jobId"] = recent.JobId.ToString() },
            overLong);

        var messages = await NewHandler().ResumeAsync(resume);

        await _applications.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _state.Received(1).ClearAsync(OwnerChat, Arg.Any<CancellationToken>());
        messages.ShouldHaveSingleItem().Text.ShouldContain("too long", Case.Insensitive);
    }

    [Fact]
    public async Task A_resumed_reply_for_an_untracked_application_says_none_tracks_it_anymore()
    {
        var recent = Entry("Staff Backend Engineer", "Stripe", daysAgo: 1);
        PipelineReturns(recent);
        // The confirmation resolves the target (still listed) but the write finds nothing to attach to.
        _applications.FindByJobAsync(recent.JobId, Arg.Any<CancellationToken>()).Returns((App?)null);
        var resume = new CommandResumeRequest(
            OwnerChat, "text",
            new Dictionary<string, string> { ["jobId"] = recent.JobId.ToString() },
            "note body");

        var messages = await NewHandler().ResumeAsync(resume);

        await _applications.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        messages.ShouldHaveSingleItem().Text.ShouldContain("anymore", Case.Insensitive);
    }

    [Fact]
    public async Task A_resumed_reply_with_a_blank_body_falls_through_to_nothing_to_note()
    {
        var recent = Entry("Staff Backend Engineer", "Stripe", daysAgo: 1);
        PipelineReturns(recent);
        _applications.FindByJobAsync(recent.JobId, Arg.Any<CancellationToken>())
            .Returns(TrackedApplication(recent.JobId));
        var resume = new CommandResumeRequest(
            OwnerChat, "text",
            new Dictionary<string, string> { ["jobId"] = recent.JobId.ToString() },
            "   ");

        var messages = await NewHandler().ResumeAsync(resume);

        // A blank reply writes nothing and renders the same helpful line rather than an empty message.
        await _applications.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _state.Received(1).ClearAsync(OwnerChat, Arg.Any<CancellationToken>());
        messages.ShouldHaveSingleItem().Text.ShouldContain("note", Case.Insensitive);
    }
}
