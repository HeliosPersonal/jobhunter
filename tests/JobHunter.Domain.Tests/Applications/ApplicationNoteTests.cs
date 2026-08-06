using JobHunter.Domain.Applications;
using Shouldly;
using Xunit;
using App = JobHunter.Domain.Applications.Application;

namespace JobHunter.Domain.Tests.Applications;

/// <summary>
/// T01/T02: free-text notes annotate an application (F6 [[data-model]] §application_notes). A note is
/// capped at 4 000 characters and touches <c>last_activity_at</c> — a note is activity — but never the
/// status. The body is never logged, only its length (invariant 12).
/// </summary>
public sealed class ApplicationNoteTests
{
    private static readonly Guid Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Job = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid NoteId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTimeOffset T0 = new(2026, 8, 6, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Adding_a_note_records_it_and_counts_as_activity_without_changing_status()
    {
        var app = App.Create(Id, Job, T0, TransitionSource.Telegram);

        var note = app.AddNote(NoteId, "Recruiter called; onsite next week.", T0.AddDays(1));

        note.Body.ShouldBe("Recruiter called; onsite next week.");
        note.CreatedAt.ShouldBe(T0.AddDays(1));
        app.Notes.ShouldHaveSingleItem().ShouldBe(note);
        app.Status.ShouldBe(ApplicationStatus.New);
        app.LastActivityAt.ShouldBe(T0.AddDays(1));
    }

    [Fact]
    public void A_note_over_four_thousand_characters_is_rejected()
    {
        var app = App.Create(Id, Job, T0, TransitionSource.Telegram);

        Should.Throw<ArgumentException>(() => app.AddNote(NoteId, new string('n', 4001), T0.AddDays(1)));
    }

    [Fact]
    public void A_note_of_exactly_four_thousand_characters_is_accepted()
    {
        var app = App.Create(Id, Job, T0, TransitionSource.Telegram);

        var note = app.AddNote(NoteId, new string('n', 4000), T0.AddDays(1));

        note.Body.Length.ShouldBe(4000);
    }

    [Fact]
    public void A_blank_note_is_rejected()
    {
        var app = App.Create(Id, Job, T0, TransitionSource.Telegram);

        Should.Throw<ArgumentException>(() => app.AddNote(NoteId, "   ", T0.AddDays(1)));
    }
}
