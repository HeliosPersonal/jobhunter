using JobHunter.Domain.Pipeline;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Pipeline;

public sealed class RunTests
{
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");

    private static Run NewRun(FakeClock? clock = null, decimal ceiling = 2.00m)
    {
        var c = clock ?? new FakeClock();
        var from = c.UtcNow - TimeSpan.FromDays(1);
        return new Run(RunId, from, c.UtcNow, ceiling, c.UtcNow);
    }

    [Fact]
    public void New_run_starts_in_Created_with_a_snapshotted_ceiling()
    {
        var clock = new FakeClock();

        var run = NewRun(clock);

        run.State.ShouldBe(RunState.Created);
        run.CeilingUsd.ShouldBe(2.00m);
        run.SpentUsd.ShouldBe(0m);
        run.JobsInScope.ShouldBe(0);
        run.JobsCarriedOver.ShouldBe(0);
        run.FinishedAt.ShouldBeNull();
        run.FailureReason.ShouldBeNull();
        run.StartedAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void Constructor_rejects_an_inverted_cutoff_window()
    {
        var clock = new FakeClock();

        Should.Throw<ArgumentException>(() =>
            new Run(RunId, clock.UtcNow, clock.UtcNow - TimeSpan.FromDays(1), 2.00m, clock.UtcNow));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_rejects_a_non_positive_ceiling(int ceiling)
    {
        var clock = new FakeClock();

        Should.Throw<ArgumentOutOfRangeException>(() =>
            new Run(RunId, clock.UtcNow, clock.UtcNow, ceiling, clock.UtcNow));
    }

    [Fact]
    public void Constructor_rejects_an_empty_id()
    {
        var clock = new FakeClock();

        Should.Throw<ArgumentException>(() =>
            new Run(Guid.Empty, clock.UtcNow, clock.UtcNow, 2.00m, clock.UtcNow));
    }

    [Fact]
    public void A_legal_transition_advances_the_state()
    {
        var clock = new FakeClock();
        var run = NewRun(clock);

        var result = run.TransitionTo(RunState.Enriching, clock.UtcNow);

        result.IsSuccess.ShouldBeTrue();
        run.State.ShouldBe(RunState.Enriching);
        run.FinishedAt.ShouldBeNull();
    }

    [Fact]
    public void Reaching_a_terminal_state_stamps_finished_at()
    {
        var clock = new FakeClock();
        var run = NewRun(clock);
        Walk(run, clock, RunState.Enriching, RunState.Matching, RunState.Ranking, RunState.Researching, RunState.Reporting);

        var result = run.TransitionTo(RunState.Delivered, clock.UtcNow);

        result.IsSuccess.ShouldBeTrue();
        run.State.ShouldBe(RunState.Delivered);
        run.FinishedAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void An_illegal_transition_is_rejected_and_names_the_attempted_pair()
    {
        var clock = new FakeClock();
        var run = NewRun(clock);

        var result = run.TransitionTo(RunState.Delivered, clock.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(Run.IllegalTransition.Code);
        result.Error.Message.ShouldContain("Created");
        result.Error.Message.ShouldContain("Delivered");
        run.State.ShouldBe(RunState.Created);
    }

    [Fact]
    public void A_terminal_run_rejects_any_further_transition()
    {
        var clock = new FakeClock();
        var run = NewRun(clock);
        run.Abort("done", clock.UtcNow, costBreach: false);

        var result = run.TransitionTo(RunState.Matching, clock.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(Run.AlreadyTerminal.Code);
    }

    [Fact]
    public void Exhaustive_walk_of_all_state_pairs_matches_the_transition_table()
    {
        var clock = new FakeClock();
        var states = Enum.GetValues<RunState>();

        foreach (var from in states)
        {
            foreach (var to in states)
            {
                var run = NewRun(clock);
                Force(run, from);

                var result = run.TransitionTo(to, clock.UtcNow);

                var expectedLegal = !RunTransitions.IsTerminal(from) && RunTransitions.IsLegal(from, to);
                result.IsSuccess.ShouldBe(
                    expectedLegal,
                    $"transition {from} -> {to} legality mismatch");
            }
        }
    }

    [Fact]
    public void Abort_from_a_non_terminal_state_records_a_reason_and_finish()
    {
        var clock = new FakeClock();
        var run = NewRun(clock);
        run.TransitionTo(RunState.Enriching, clock.UtcNow);

        run.Abort("provider outage", clock.UtcNow, costBreach: false);

        run.State.ShouldBe(RunState.Failed);
        run.FailureReason.ShouldBe("provider outage");
        run.FinishedAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void Abort_with_a_cost_breach_produces_CostAborted()
    {
        var clock = new FakeClock();
        var run = NewRun(clock);
        run.TransitionTo(RunState.Enriching, clock.UtcNow);

        run.Abort("ceiling would be breached", clock.UtcNow, costBreach: true);

        run.State.ShouldBe(RunState.CostAborted);
        run.FailureReason.ShouldBe("ceiling would be breached");
    }

    [Fact]
    public void Abort_is_idempotent_and_keeps_the_first_reason()
    {
        var clock = new FakeClock();
        var run = NewRun(clock);
        run.Abort("first", clock.UtcNow, costBreach: true);
        var firstFinish = run.FinishedAt;

        clock.Advance(TimeSpan.FromHours(1));
        run.Abort("second", clock.UtcNow, costBreach: false);

        run.State.ShouldBe(RunState.CostAborted);
        run.FailureReason.ShouldBe("first");
        run.FinishedAt.ShouldBe(firstFinish);
    }

    [Fact]
    public void Abort_rejects_a_blank_reason()
    {
        var clock = new FakeClock();
        var run = NewRun(clock);

        Should.Throw<ArgumentException>(() => run.Abort(" ", clock.UtcNow, costBreach: false));
    }

    [Fact]
    public void Scope_carry_over_and_spend_are_recorded()
    {
        var clock = new FakeClock();
        var run = NewRun(clock);

        run.SetScope(42);
        run.RecordCarryOver(3);
        run.SetSpend(0.43m);

        run.JobsInScope.ShouldBe(42);
        run.JobsCarriedOver.ShouldBe(3);
        run.SpentUsd.ShouldBe(0.43m);
    }

    [Fact]
    public void Scope_carry_over_and_spend_reject_negatives()
    {
        var clock = new FakeClock();
        var run = NewRun(clock);

        Should.Throw<ArgumentOutOfRangeException>(() => run.SetScope(-1));
        Should.Throw<ArgumentOutOfRangeException>(() => run.RecordCarryOver(-1));
        Should.Throw<ArgumentOutOfRangeException>(() => run.SetSpend(-0.01m));
    }

    private static void Walk(Run run, FakeClock clock, params RunState[] states)
    {
        foreach (var state in states)
        {
            run.TransitionTo(state, clock.UtcNow).IsSuccess.ShouldBeTrue();
        }
    }

    /// <summary>
    /// Drives a fresh Run to <paramref name="target"/> along the canonical happy path so the exhaustive
    /// pair walk can start each iteration from a real, reachable state rather than a reflected one.
    /// </summary>
    private static void Force(Run run, RunState target)
    {
        var clock = new FakeClock();
        switch (target)
        {
            case RunState.Created:
                break;
            case RunState.Enriching:
                Walk(run, clock, RunState.Enriching);
                break;
            case RunState.Matching:
                Walk(run, clock, RunState.Enriching, RunState.Matching);
                break;
            case RunState.Ranking:
                Walk(run, clock, RunState.Enriching, RunState.Matching, RunState.Ranking);
                break;
            case RunState.Researching:
                Walk(run, clock, RunState.Enriching, RunState.Matching, RunState.Ranking, RunState.Researching);
                break;
            case RunState.Reporting:
                Walk(run, clock, RunState.Enriching, RunState.Matching, RunState.Ranking, RunState.Researching, RunState.Reporting);
                break;
            case RunState.Delivered:
                Walk(run, clock, RunState.Enriching, RunState.Matching, RunState.Ranking, RunState.Researching, RunState.Reporting, RunState.Delivered);
                break;
            case RunState.Failed:
                run.Abort("forced", clock.UtcNow, costBreach: false);
                break;
            case RunState.CostAborted:
                run.Abort("forced", clock.UtcNow, costBreach: true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, "Unhandled RunState.");
        }

        run.State.ShouldBe(target);
    }
}
