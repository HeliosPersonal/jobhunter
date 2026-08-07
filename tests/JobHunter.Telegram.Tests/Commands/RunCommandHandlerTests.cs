using JobHunter.Application.Enrichment;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Commands;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Telegram.Commands;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// <c>/run</c> (catalogue §Operations · Sensitive · ✎): triggers the daily pipeline off-schedule. Refused outright
/// while a Run is live (invariant: one live Run — <see cref="IRunRepository.FindActiveRunAsync"/>), so a second Run
/// is never started. Otherwise it previews: it reproduces the scope the orchestrator would — jobs first seen since
/// the last Run's cut-off, or the initial look-back when there is none — and names the honest cost cap, the
/// snapshotted <see cref="RunOptions.CeilingUsd"/>. State-changing: it stores a pending <see cref="ConversationState"/>
/// and asks; the confirm tap that publishes <c>StartDailyRun</c> is wired in T10, exactly as <c>/floor</c>'s is.
/// </summary>
public sealed class RunCommandHandlerTests
{
    private const long OwnerChat = 4242;
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 6, 0, 0, TimeSpan.Zero);

    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly ILiveJobsQuery _liveJobs = Substitute.For<ILiveJobsQuery>();
    private readonly IConversationStateStore _state = Substitute.For<IConversationStateStore>();
    private readonly FakeClock _clock = new(Now);
    private readonly RunOptions _options = new() { CeilingUsd = 2.00m, InitialLookBack = TimeSpan.FromHours(24) };

    private RunCommandHandler NewHandler() =>
        new(_runs, _liveJobs, _state, _clock, _options, NullLogger<RunCommandHandler>.Instance);

    private static Run LiveRun()
    {
        var run = new Run(Guid.NewGuid(), Now.AddHours(-1), Now, ceilingUsd: 2.00m, Now.AddHours(-1));
        run.SetScope(10);
        run.TransitionTo(RunState.Enriching, Now.AddHours(-1));
        return run;
    }

    private static LiveJob Job(DateTimeOffset firstSeen) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Staff Engineer", "Staff", "Remote", "FullTime",
            "https://example.test/apply", firstSeen, firstSeen);

    private void NoRunLive() => _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns((Run?)null);
    private void LastCutoff(DateTimeOffset? cutoff) =>
        _runs.FindMostRecentCutoffAsync(Arg.Any<CancellationToken>()).Returns(cutoff);
    private void Discovers(params LiveJob[] jobs) =>
        _liveJobs.DiscoveredSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(jobs);

    [Fact]
    public async Task It_refuses_when_a_run_is_already_live()
    {
        _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns(LiveRun());

        var text = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null)))[0].Text;

        text.ShouldContain("already", Case.Insensitive);
        // A refusal previews nothing and stores no confirm state — the live Run is untouched.
        await _liveJobs.DidNotReceive().DiscoveredSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _state.DidNotReceive().SetAsync(Arg.Any<long>(), Arg.Any<ConversationState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task It_previews_the_number_of_jobs_in_scope()
    {
        NoRunLive();
        LastCutoff(Now.AddHours(-24));
        Discovers(Job(Now.AddHours(-2)), Job(Now.AddHours(-1)));

        var text = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null)))[0].Text;

        text.ShouldContain("2");
    }

    [Fact]
    public async Task It_reproduces_the_scope_from_the_last_cutoff()
    {
        NoRunLive();
        var cutoff = Now.AddHours(-30);
        LastCutoff(cutoff);
        Discovers(Job(Now.AddHours(-1)));

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        await _liveJobs.Received(1).DiscoveredSinceAsync(cutoff, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task It_falls_back_to_the_initial_lookback_when_no_cutoff_exists()
    {
        NoRunLive();
        LastCutoff(null);
        Discovers();

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        // No previous Run to inherit a cut-off from: the very first Run's window, now minus the initial look-back.
        await _liveJobs.Received(1).DiscoveredSinceAsync(Now.AddHours(-24), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task It_excludes_jobs_first_seen_after_now()
    {
        NoRunLive();
        LastCutoff(Now.AddHours(-24));
        // A job dated in the future is not yet in scope — only jobs first seen at or before now count.
        Discovers(Job(Now.AddHours(-1)), Job(Now.AddHours(1)));

        var text = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null)))[0].Text;

        text.ShouldContain("1");
    }

    [Fact]
    public async Task It_names_the_configured_cost_ceiling()
    {
        NoRunLive();
        LastCutoff(Now.AddHours(-24));
        Discovers(Job(Now.AddHours(-1)));

        var text = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null)))[0].Text;

        // The honest cap the Run would be created under — the snapshotted ceiling, two decimals. The one MarkdownV2
        // escaper escapes the point, so the rendered figure reads 2\.00.
        text.ShouldContain("2\\.00");
    }

    [Fact]
    public async Task It_stores_a_pending_confirm_state_for_the_resume_step()
    {
        NoRunLive();
        LastCutoff(Now.AddHours(-24));
        Discovers(Job(Now.AddHours(-1)));

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        await _state.Received(1).SetAsync(
            OwnerChat, Arg.Is<ConversationState>(s => s != null && s.Command == "run"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => NewHandler().HandleAsync(null!));
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        Should.Throw<ArgumentNullException>(() => new RunCommandHandler(null!, _liveJobs, _state, _clock, _options, NullLogger<RunCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new RunCommandHandler(_runs, null!, _state, _clock, _options, NullLogger<RunCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new RunCommandHandler(_runs, _liveJobs, null!, _clock, _options, NullLogger<RunCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new RunCommandHandler(_runs, _liveJobs, _state, null!, _options, NullLogger<RunCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new RunCommandHandler(_runs, _liveJobs, _state, _clock, null!, NullLogger<RunCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new RunCommandHandler(_runs, _liveJobs, _state, _clock, _options, null!));
    }
}
