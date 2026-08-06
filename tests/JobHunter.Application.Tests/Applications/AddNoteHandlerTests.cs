using JobHunter.Application.Applications;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using JobHunter.Application.Tests.Support;
using JobHunter.TestKit;
using Shouldly;
using Xunit;
using App = JobHunter.Domain.Applications.Application;

namespace JobHunter.Application.Tests.Applications;

/// <summary>
/// T07: a free-text note is attached to an application and appears in its history (AC-06). The handler is the
/// single write path both the Telegram <c>/note</c> command and the API <c>POST …/notes</c> drive, so its
/// outcomes are values, not exceptions (coding-standards §4): an over-long or blank note is refused with a
/// distinct outcome the caller turns into a clear message, and a note for an untracked job is refused rather
/// than silently creating an application. The note body is <b>never logged</b> — only its length — because a
/// note may contain anything the Owner typed (invariant 12, done-when 3).
/// </summary>
public sealed class AddNoteHandlerTests
{
    private static readonly Guid Job = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTimeOffset T0 = new(2026, 8, 6, 7, 0, 0, TimeSpan.Zero);

    private readonly FakeApplicationRepository _repo = new();
    private readonly SequentialIdGenerator _ids = new();
    private readonly CapturingLogger<AddNoteHandler> _logger = new();

    private AddNoteHandler CreateHandler() => new(_repo, _ids, _logger);

    private Task<AddNoteOutcome> Handle(string body, DateTimeOffset at) =>
        CreateHandler().Handle(new AddNoteCommand(Job, body, at), CancellationToken.None);

    private static App Tracked() =>
        App.Create(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), Job, T0, TransitionSource.Telegram);

    [Fact]
    public async Task A_note_is_stored_with_its_time_and_counts_as_activity()
    {
        var app = Tracked();
        _repo.Seed(app);

        var outcome = await Handle("Recruiter called; onsite next week.", T0.AddDays(1));

        outcome.ShouldBe(AddNoteOutcome.Recorded);
        var note = app.Notes.ShouldHaveSingleItem();
        note.Body.ShouldBe("Recruiter called; onsite next week.");
        note.CreatedAt.ShouldBe(T0.AddDays(1));
        // A note is activity — it advances last_activity_at — but never changes the status.
        app.LastActivityAt.ShouldBe(T0.AddDays(1));
        app.Status.ShouldBe(ApplicationStatus.New);
        _repo.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_note_over_the_cap_is_refused_and_nothing_is_written()
    {
        var app = Tracked();
        _repo.Seed(app);

        var outcome = await Handle(new string('n', ApplicationNote.MaxLength + 1), T0.AddDays(1));

        outcome.ShouldBe(AddNoteOutcome.TooLong);
        app.Notes.ShouldBeEmpty();
        _repo.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_note_of_exactly_the_cap_is_accepted()
    {
        var app = Tracked();
        _repo.Seed(app);

        var outcome = await Handle(new string('n', ApplicationNote.MaxLength), T0.AddDays(1));

        outcome.ShouldBe(AddNoteOutcome.Recorded);
        app.Notes.ShouldHaveSingleItem().Body.Length.ShouldBe(ApplicationNote.MaxLength);
    }

    [Fact]
    public async Task A_blank_note_is_refused_and_nothing_is_written()
    {
        var app = Tracked();
        _repo.Seed(app);

        var outcome = await Handle("   ", T0.AddDays(1));

        outcome.ShouldBe(AddNoteOutcome.Empty);
        app.Notes.ShouldBeEmpty();
        _repo.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_note_for_an_untracked_job_is_refused_and_creates_no_application()
    {
        var outcome = await Handle("Note for a job with no application yet.", T0.AddDays(1));

        outcome.ShouldBe(AddNoteOutcome.ApplicationNotFound);
        _repo.Count.ShouldBe(0);
        _repo.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task The_note_body_never_appears_in_a_log_line()
    {
        var app = Tracked();
        _repo.Seed(app);
        const string secret = "salary is 250k and my password is hunter2";

        await Handle(secret, T0.AddDays(1));

        // Every log line may mention the note's length but never a fragment of its content (done-when 3).
        _logger.Entries.ShouldNotBeEmpty();
        foreach (var (_, message) in _logger.Entries)
        {
            message.ShouldNotContain("250k");
            message.ShouldNotContain("hunter2");
            message.ShouldNotContain(secret);
        }
    }

    /// <summary>
    /// The same small stateful <see cref="IApplicationRepository"/> double the owner-action tests use: it
    /// returns the seeded aggregate by job id so the handler mutates a real <see cref="App"/> and its note
    /// collection, without a database. The real-database proof of AC-06 lives in the integration suite.
    /// </summary>
    private sealed class FakeApplicationRepository : IApplicationRepository
    {
        private readonly List<App> _applications = [];

        public int Count => _applications.Count;

        public int SaveCount { get; private set; }

        public void Seed(App application) => _applications.Add(application);

        public void Add(App application) => _applications.Add(application);

        public Task<App?> FindByJobAsync(Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_applications.Find(a => a.JobId == jobId));

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(0);
        }
    }
}
