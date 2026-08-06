using JobHunter.Domain.Applications;
using Shouldly;
using Xunit;
using App = JobHunter.Domain.Applications.Application;

namespace JobHunter.Domain.Tests.Applications;

/// <summary>
/// T01: the application aggregate. It is created lazily in <see cref="ApplicationStatus.New"/> with a
/// creating transition, advances only along permitted transitions (recording each as append-only
/// history, QG-1), stamps <c>applied_at</c> exactly once, and treats a closed posting as metadata that
/// never touches the status (AC-07).
/// </summary>
public sealed class ApplicationTests
{
    private static readonly Guid Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Job = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset T0 = new(2026, 8, 6, 7, 0, 0, TimeSpan.Zero);
    private static readonly ReminderPolicy Policy = ReminderPolicy.Default;

    [Fact]
    public void A_new_application_starts_in_New_with_one_creating_transition()
    {
        var app = App.Create(Id, Job, T0, TransitionSource.Telegram);

        app.Id.ShouldBe(Id);
        app.JobId.ShouldBe(Job);
        app.Status.ShouldBe(ApplicationStatus.New);
        app.PostingClosed.ShouldBeFalse();
        app.Archived.ShouldBeFalse();
        app.AppliedAt.ShouldBeNull();
        app.CreatedAt.ShouldBe(T0);
        app.LastActivityAt.ShouldBe(T0);

        app.Transitions.Count.ShouldBe(1);
        var creating = app.Transitions[0];
        creating.From.ShouldBeNull();
        creating.To.ShouldBe(ApplicationStatus.New);
        creating.Source.ShouldBe(TransitionSource.Telegram);
        creating.OccurredAt.ShouldBe(T0);
    }

    [Fact]
    public void A_permitted_change_advances_the_status_and_appends_a_transition()
    {
        var app = App.Create(Id, Job, T0, TransitionSource.Telegram);

        var result = app.ChangeStatus(ApplicationStatus.Saved, TransitionSource.Telegram, T0.AddMinutes(1), Policy);

        result.IsSuccess.ShouldBeTrue();
        app.Status.ShouldBe(ApplicationStatus.Saved);
        app.LastActivityAt.ShouldBe(T0.AddMinutes(1));
        app.Transitions.Count.ShouldBe(2);
        app.Transitions[1].From.ShouldBe(ApplicationStatus.New);
        app.Transitions[1].To.ShouldBe(ApplicationStatus.Saved);
    }

    [Fact]
    public void A_refused_change_leaves_the_status_and_history_untouched_and_returns_the_remedy()
    {
        var app = App.Create(Id, Job, T0, TransitionSource.Telegram);
        app.ChangeStatus(ApplicationStatus.Rejected, TransitionSource.Telegram, T0.AddMinutes(1), Policy);

        // Rejected → Interview is refused by the contract matrix.
        var result = app.ChangeStatus(ApplicationStatus.Interview, TransitionSource.Api, T0.AddMinutes(2), Policy);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("TransitionNotPermitted");
        result.Error.Message.ShouldContain("new application");
        app.Status.ShouldBe(ApplicationStatus.Rejected);
        app.Transitions.Count.ShouldBe(2);
        app.LastActivityAt.ShouldBe(T0.AddMinutes(1));
    }

    [Fact]
    public void Applied_at_is_stamped_on_first_entry_to_Applied()
    {
        var app = App.Create(Id, Job, T0, TransitionSource.Telegram);

        app.ChangeStatus(ApplicationStatus.Applied, TransitionSource.Telegram, T0.AddMinutes(1), Policy);

        app.AppliedAt.ShouldBe(T0.AddMinutes(1));
    }

    [Fact]
    public void Applied_at_is_never_changed_once_set()
    {
        var app = App.Create(Id, Job, T0, TransitionSource.Telegram);
        app.ChangeStatus(ApplicationStatus.Applied, TransitionSource.Telegram, T0.AddMinutes(1), Policy);
        var firstAppliedAt = app.AppliedAt;

        // A correction away and a re-affirmation back must not move applied_at.
        app.ChangeStatus(ApplicationStatus.Saved, TransitionSource.Telegram, T0.AddHours(1), Policy);
        app.ChangeStatus(ApplicationStatus.Applied, TransitionSource.Telegram, T0.AddHours(2), Policy);

        app.AppliedAt.ShouldBe(firstAppliedAt);
        app.AppliedAt.ShouldBe(T0.AddMinutes(1));
    }

    [Fact]
    public void A_self_transition_is_a_permitted_no_op_that_still_records_a_round()
    {
        var app = App.Create(Id, Job, T0, TransitionSource.Telegram);
        app.ChangeStatus(ApplicationStatus.Interview, TransitionSource.Telegram, T0.AddMinutes(1), Policy);

        var result = app.ChangeStatus(ApplicationStatus.Interview, TransitionSource.Telegram, T0.AddDays(3), Policy);

        result.IsSuccess.ShouldBeTrue();
        app.Status.ShouldBe(ApplicationStatus.Interview);
        // Interview → Interview is a further round, visible as its own transition.
        app.Transitions.Count.ShouldBe(3);
    }

    [Fact]
    public void Marking_the_posting_closed_records_a_system_self_transition_without_changing_status()
    {
        var app = App.Create(Id, Job, T0, TransitionSource.Telegram);
        app.ChangeStatus(ApplicationStatus.Applied, TransitionSource.Telegram, T0.AddMinutes(1), Policy);
        var transitionsBefore = app.Transitions.Count;

        app.MarkPostingClosed(T0.AddDays(1));

        app.PostingClosed.ShouldBeTrue();
        // AC-07: the status is untouched — a closed posting tells us nothing about the Owner's application.
        app.Status.ShouldBe(ApplicationStatus.Applied);
        app.LastActivityAt.ShouldBe(T0.AddDays(1));

        // The closure is recorded as history: a System-sourced self-transition (from == to) carrying detail,
        // so a System change is distinguishable from a deliberate one (SAD §8) without fabricating a move.
        app.Transitions.Count.ShouldBe(transitionsBefore + 1);
        var closure = app.Transitions[^1];
        closure.From.ShouldBe(ApplicationStatus.Applied);
        closure.To.ShouldBe(ApplicationStatus.Applied);
        closure.Source.ShouldBe(TransitionSource.System);
        closure.Detail.ShouldBe(App.PostingClosedDetail);
        closure.OccurredAt.ShouldBe(T0.AddDays(1));
    }

    [Fact]
    public void Marking_the_posting_closed_twice_records_the_closure_once()
    {
        var app = App.Create(Id, Job, T0, TransitionSource.Telegram);
        var transitionsBefore = app.Transitions.Count;

        app.MarkPostingClosed(T0.AddDays(1));
        app.MarkPostingClosed(T0.AddDays(2));

        app.PostingClosed.ShouldBeTrue();
        // The second call is a no-op: last_activity_at is not advanced and no second closure is recorded.
        app.LastActivityAt.ShouldBe(T0.AddDays(1));
        app.Transitions.Count.ShouldBe(transitionsBefore + 1);
    }

    [Fact]
    public void A_change_to_a_status_with_a_reminder_threshold_sets_next_action_at()
    {
        var app = App.Create(Id, Job, T0, TransitionSource.Telegram);

        app.ChangeStatus(ApplicationStatus.Applied, TransitionSource.Telegram, T0.AddMinutes(1), Policy);

        // Applied has a 10-day threshold by default (SAD §8).
        app.NextActionAt.ShouldBe(T0.AddMinutes(1).Add(Policy.ThresholdFor(ApplicationStatus.Applied)!.Value));
    }

    [Fact]
    public void A_change_to_a_status_without_a_reminder_threshold_clears_next_action_at()
    {
        var app = App.Create(Id, Job, T0, TransitionSource.Telegram);
        app.ChangeStatus(ApplicationStatus.Applied, TransitionSource.Telegram, T0.AddMinutes(1), Policy);
        app.NextActionAt.ShouldNotBeNull();

        // Rejected is terminal — nothing to chase, so no reminder is scheduled.
        app.ChangeStatus(ApplicationStatus.Rejected, TransitionSource.Telegram, T0.AddDays(1), Policy);

        app.NextActionAt.ShouldBeNull();
    }

    [Theory]
    [InlineData(ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Offer)]
    [InlineData(ApplicationStatus.Ignored)]
    public void A_reached_outcome_is_terminal(ApplicationStatus terminal)
    {
        var app = App.Create(Id, Job, T0, TransitionSource.Telegram);
        // Offer is only reachable from Applied/Interview (New → Offer is refused), so pass through Applied.
        app.ChangeStatus(ApplicationStatus.Applied, TransitionSource.Telegram, T0.AddMinutes(1), Policy);
        app.ChangeStatus(terminal, TransitionSource.Telegram, T0.AddMinutes(2), Policy);

        app.Status.ShouldBe(terminal);
        app.IsTerminal.ShouldBeTrue();
    }

    [Theory]
    [InlineData(ApplicationStatus.New)]
    [InlineData(ApplicationStatus.Saved)]
    [InlineData(ApplicationStatus.Applied)]
    [InlineData(ApplicationStatus.Interview)]
    public void An_open_stage_is_not_terminal(ApplicationStatus open)
    {
        var app = App.Create(Id, Job, T0, TransitionSource.Telegram);
        if (open != ApplicationStatus.New)
        {
            app.ChangeStatus(open, TransitionSource.Telegram, T0.AddMinutes(1), Policy);
        }

        app.IsTerminal.ShouldBeFalse();
    }

    [Fact]
    public void Create_rejects_an_empty_job_id()
    {
        Should.Throw<ArgumentException>(
            () => App.Create(Id, Guid.Empty, T0, TransitionSource.Telegram));
    }
}
