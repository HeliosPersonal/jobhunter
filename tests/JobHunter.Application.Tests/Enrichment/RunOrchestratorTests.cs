using JobHunter.Application.Enrichment;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;

namespace JobHunter.Application.Tests.Enrichment;

/// <summary>
/// T09: the start, scope and resume half of the Run machinery (F3 SAD §6.1, ADR-F3-0001). The repository
/// and the live-jobs read are substituted, so these are zero-database unit tests. They assert the four
/// behaviours the task's Done-when calls out — a window that catches up a skipped day, an empty scope
/// completing without submission, the active-Run guard, and a resume that polls rather than resubmits
/// (AC-05) — plus that no submission-committing message is emitted before the cost gate (T10 owns spend).
/// </summary>
public sealed class RunOrchestratorTests
{
    private static readonly DateTimeOffset WindowEnd = new(2026, 8, 3, 2, 0, 0, TimeSpan.Zero);

    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly ILiveJobsQuery _liveJobs = Substitute.For<ILiveJobsQuery>();
    private readonly FakeClock _clock = new(WindowEnd.AddSeconds(1));
    private readonly SequentialIdGenerator _ids = new();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly RunOptions _options = new();

    private RunOrchestrator CreateOrchestrator() =>
        new(_runs, _liveJobs, _clock, _ids, NullLogger<RunOrchestrator>.Instance);

    // PublishAsync is generic, so a single typed Arg.Do would miss the other message types the
    // orchestrator emits. Reading the recorded first argument off every call captures them all.
    private List<object> Published() =>
        _bus.ReceivedCalls()
            .Select(c => c.GetArguments())
            .Where(a => a.Length > 0 && a[0] is not null)
            .Select(a => a[0]!)
            .ToList();

    private static LiveJob JobSeenAt(DateTimeOffset firstSeenAt) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), "Backend Engineer", "Senior", "Remote",
            "FullTime", "https://example.com/apply", firstSeenAt, firstSeenAt);

    // ---- Start: window, scope, hand-off -------------------------------------------------------

    [Fact]
    public async Task Start_derives_cutoff_from_from_the_previous_runs_cutoff_to()
    {
        var previousCutoff = WindowEnd.AddDays(-1);
        _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns((Run?)null);
        _runs.FindMostRecentCutoffAsync(Arg.Any<CancellationToken>()).Returns(previousCutoff);
        _liveJobs.DiscoveredSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([JobSeenAt(WindowEnd.AddHours(-2))]);

        Run? added = null;
        _runs.When(r => r.Add(Arg.Any<Run>())).Do(ci => added = ci.Arg<Run>());

        await CreateOrchestrator().Handle(new StartDailyRun(WindowEnd), _bus, _options, CancellationToken.None);

        added.ShouldNotBeNull();
        added.CutoffFrom.ShouldBe(previousCutoff);
        added.CutoffTo.ShouldBe(WindowEnd);
    }

    [Fact]
    public async Task Start_with_no_previous_run_uses_the_configured_look_back()
    {
        _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns((Run?)null);
        _runs.FindMostRecentCutoffAsync(Arg.Any<CancellationToken>()).Returns((DateTimeOffset?)null);
        _liveJobs.DiscoveredSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([JobSeenAt(WindowEnd.AddHours(-1))]);

        Run? added = null;
        _runs.When(r => r.Add(Arg.Any<Run>())).Do(ci => added = ci.Arg<Run>());

        await CreateOrchestrator().Handle(new StartDailyRun(WindowEnd), _bus, _options, CancellationToken.None);

        added.ShouldNotBeNull();
        added.CutoffFrom.ShouldBe(WindowEnd - _options.InitialLookBack);
    }

    [Fact]
    public async Task Start_snapshots_the_ceiling_and_selects_the_scope()
    {
        _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns((Run?)null);
        _runs.FindMostRecentCutoffAsync(Arg.Any<CancellationToken>()).Returns((DateTimeOffset?)null);
        _liveJobs.DiscoveredSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([JobSeenAt(WindowEnd.AddHours(-3)), JobSeenAt(WindowEnd.AddHours(-4))]);

        Run? added = null;
        _runs.When(r => r.Add(Arg.Any<Run>())).Do(ci => added = ci.Arg<Run>());

        await CreateOrchestrator().Handle(new StartDailyRun(WindowEnd), _bus, _options, CancellationToken.None);

        added.ShouldNotBeNull();
        added.CeilingUsd.ShouldBe(_options.CeilingUsd);
        added.JobsInScope.ShouldBe(2);
    }

    [Fact]
    public async Task Scope_excludes_jobs_first_seen_after_the_cutoff_to()
    {
        _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns((Run?)null);
        _runs.FindMostRecentCutoffAsync(Arg.Any<CancellationToken>()).Returns((DateTimeOffset?)null);
        _liveJobs.DiscoveredSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([
                JobSeenAt(WindowEnd.AddHours(-1)),   // in window
                JobSeenAt(WindowEnd.AddMinutes(30)), // after cutoff_to — belongs to the next Run
            ]);

        Run? added = null;
        _runs.When(r => r.Add(Arg.Any<Run>())).Do(ci => added = ci.Arg<Run>());

        await CreateOrchestrator().Handle(new StartDailyRun(WindowEnd), _bus, _options, CancellationToken.None);

        added.ShouldNotBeNull();
        added.JobsInScope.ShouldBe(1);
    }

    [Fact]
    public async Task Start_with_jobs_in_scope_hands_off_to_submission_and_emits_run_started()
    {
        _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns((Run?)null);
        _runs.FindMostRecentCutoffAsync(Arg.Any<CancellationToken>()).Returns((DateTimeOffset?)null);
        _liveJobs.DiscoveredSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([JobSeenAt(WindowEnd.AddHours(-2))]);

        await CreateOrchestrator().Handle(new StartDailyRun(WindowEnd), _bus, _options, CancellationToken.None);

        Published().OfType<RunStarted>().ShouldHaveSingleItem().JobsInScope.ShouldBe(1);
        Published().OfType<EnrichmentSubmissionDue>().ShouldHaveSingleItem();
    }

    // ---- Empty scope: complete without submitting ---------------------------------------------

    [Fact]
    public async Task Empty_scope_completes_enrichment_without_submitting()
    {
        _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns((Run?)null);
        _runs.FindMostRecentCutoffAsync(Arg.Any<CancellationToken>()).Returns((DateTimeOffset?)null);
        _liveJobs.DiscoveredSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);

        Run? added = null;
        _runs.When(r => r.Add(Arg.Any<Run>())).Do(ci => added = ci.Arg<Run>());

        await CreateOrchestrator().Handle(new StartDailyRun(WindowEnd), _bus, _options, CancellationToken.None);

        added.ShouldNotBeNull();
        added.JobsInScope.ShouldBe(0);
        added.State.ShouldBe(RunState.Matching);
        Published().OfType<EnrichmentSubmissionDue>().ShouldBeEmpty();
        Published().OfType<EnrichmentCompleted>().ShouldHaveSingleItem().EnrichedCount.ShouldBe(0);
    }

    // ---- Active-Run guard ----------------------------------------------------------------------

    [Fact]
    public async Task A_second_run_is_not_created_while_one_is_live()
    {
        var live = new Run(_ids.NewId(), WindowEnd.AddDays(-1), WindowEnd, 2.00m, WindowEnd);
        _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns(live);

        await CreateOrchestrator().Handle(new StartDailyRun(WindowEnd), _bus, _options, CancellationToken.None);

        _runs.DidNotReceive().Add(Arg.Any<Run>());
        Published().ShouldBeEmpty();
    }

    // ---- Resume --------------------------------------------------------------------------------

    [Fact]
    public async Task Resume_of_a_created_run_recomputes_scope_and_continues()
    {
        var run = new Run(_ids.NewId(), WindowEnd.AddDays(-1), WindowEnd, 2.00m, WindowEnd);
        _runs.FindAsync(run.Id, Arg.Any<CancellationToken>()).Returns(run);
        _liveJobs.DiscoveredSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([JobSeenAt(WindowEnd.AddHours(-2))]);

        await CreateOrchestrator().Handle(new ResumeRun(run.Id), _bus, CancellationToken.None);

        run.JobsInScope.ShouldBe(1);
        Published().OfType<EnrichmentSubmissionDue>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Resume_of_an_enriching_run_polls_and_never_resubmits()
    {
        var run = new Run(_ids.NewId(), WindowEnd.AddDays(-1), WindowEnd, 2.00m, WindowEnd);
        run.SetScope(10);
        run.TransitionTo(RunState.Enriching, WindowEnd);
        _runs.FindAsync(run.Id, Arg.Any<CancellationToken>()).Returns(run);

        await CreateOrchestrator().Handle(new ResumeRun(run.Id), _bus, CancellationToken.None);

        Published().OfType<BatchPollDue>().ShouldHaveSingleItem().RunId.ShouldBe(run.Id);
        Published().OfType<EnrichmentSubmissionDue>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Resume_of_a_terminal_run_does_nothing()
    {
        var run = new Run(_ids.NewId(), WindowEnd.AddDays(-1), WindowEnd, 2.00m, WindowEnd);
        run.Abort("done", WindowEnd, costBreach: false);
        _runs.FindAsync(run.Id, Arg.Any<CancellationToken>()).Returns(run);

        await CreateOrchestrator().Handle(new ResumeRun(run.Id), _bus, CancellationToken.None);

        Published().ShouldBeEmpty();
    }

    [Fact]
    public async Task Resume_of_a_downstream_stage_defers_to_its_owning_feature()
    {
        var run = new Run(_ids.NewId(), WindowEnd.AddDays(-1), WindowEnd, 2.00m, WindowEnd);
        run.SetScope(10);
        run.TransitionTo(RunState.Enriching, WindowEnd);
        run.TransitionTo(RunState.Matching, WindowEnd);
        _runs.FindAsync(run.Id, Arg.Any<CancellationToken>()).Returns(run);

        await CreateOrchestrator().Handle(new ResumeRun(run.Id), _bus, CancellationToken.None);

        Published().ShouldBeEmpty();
    }

    [Fact]
    public async Task Resume_of_an_unknown_run_is_ignored()
    {
        _runs.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Run?)null);

        await CreateOrchestrator().Handle(new ResumeRun(Guid.NewGuid()), _bus, CancellationToken.None);

        Published().ShouldBeEmpty();
    }

    [Fact]
    public async Task Startup_sweep_queues_one_resume_per_non_terminal_run()
    {
        var a = new Run(_ids.NewId(), WindowEnd.AddDays(-1), WindowEnd, 2.00m, WindowEnd);
        var b = new Run(_ids.NewId(), WindowEnd.AddDays(-1), WindowEnd, 2.00m, WindowEnd);
        _runs.FindResumableRunsAsync(Arg.Any<CancellationToken>()).Returns([a, b]);

        var count = await CreateOrchestrator().ResumeNonTerminalRunsAsync(_bus, CancellationToken.None);

        count.ShouldBe(2);
        Published().OfType<ResumeRun>().Select(m => m.RunId).ShouldBe([a.Id, b.Id]);
    }
}
