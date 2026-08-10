using JobHunter.Api.Endpoints;
using JobHunter.Application.Applications;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using JobHunter.Domain.Preferences;
using JobHunter.TestKit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;
using App = JobHunter.Domain.Applications.Application;

namespace JobHunter.Api.Tests;

/// <summary>
/// Direct-call cover for the two F6 write handlers' branch arms the host-driven
/// <see cref="ApplicationEndpointTests"/> cannot reach with an HTTP body: a null request object (a client that
/// sent no JSON), a status change whose aggregate vanished between the id resolution and the write
/// (<see cref="ChangeApplicationStatusResult.ApplicationNotFound"/>), a permitted change whose re-read comes
/// back null (the <c>?? application</c> fallback), an over-long note (<see cref="AddNoteOutcome.TooLong"/>) and a
/// note whose aggregate vanished (<see cref="AddNoteOutcome.ApplicationNotFound"/>). Each drives the real
/// handler through substituted collaborators, so the endpoint's mapping switch is exercised end-to-end with no
/// host and no network.
/// </summary>
public sealed class ApplicationEndpointsBranchTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 6, 7, 0, 0, TimeSpan.Zero);

    private readonly IApplicationHistoryQuery _history = Substitute.For<IApplicationHistoryQuery>();
    private readonly IApplicationRepository _applications = Substitute.For<IApplicationRepository>();
    private readonly IJobFactsSnapshotQuery _facts = Substitute.For<IJobFactsSnapshotQuery>();
    private readonly IOutcomeSignalWriter _signals = Substitute.For<IOutcomeSignalWriter>();
    private readonly FakeClock _clock = new(T0);

    private ChangeApplicationStatusHandler StatusHandler() => new(
        _applications,
        new OutcomeSignalPublisher(
            _facts, _signals, new SequentialIdGenerator(), SignalWeights.Default,
            NullLogger<OutcomeSignalPublisher>.Instance),
        ReminderPolicy.Default,
        NullLogger<ChangeApplicationStatusHandler>.Instance);

    private AddNoteHandler NoteHandler() => new(
        _applications, new SequentialIdGenerator(), NullLogger<AddNoteHandler>.Instance);

    private static ApplicationHistory HistoryFor(Guid appId, Guid jobId, ApplicationStatus status) => new(
        appId, jobId, "Staff Backend Engineer", "Snowflake", status,
        PostingClosed: false, Archived: false, AppliedAt: null, LastActivityAt: T0, NextActionAt: null,
        Transitions: [], Notes: []);

    [Fact]
    public async Task Status_change_with_a_null_request_body_is_a_400_problem()
    {
        // A client that sent no JSON: request binds to null, so the short-circuit arm of the guard fires and the
        // detail string reads the null-conditional ToStatus — neither reachable through a typed HTTP body.
        var result = await ApplicationEndpoints.HandleStatusChangeAsync(
            Guid.NewGuid(), request: null, _history, StatusHandler(), _clock, CancellationToken.None);

        var problem = result.ShouldBeOfType<ProblemHttpResult>();
        problem.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        await _history.DidNotReceive().HistoryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Status_change_whose_aggregate_vanished_after_resolution_is_a_404()
    {
        var appId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        // The id resolves (history is non-null), but the write repository no longer tracks the job, so the
        // handler returns ApplicationNotFound and the endpoint maps that switch arm to a 404.
        _history.HistoryAsync(appId, Arg.Any<CancellationToken>())
            .Returns(HistoryFor(appId, jobId, ApplicationStatus.Saved));
        _applications.FindByJobAsync(jobId, Arg.Any<CancellationToken>()).Returns((App?)null);

        var result = await ApplicationEndpoints.HandleStatusChangeAsync(
            appId, new StatusChangeRequest("Interview", null), _history, StatusHandler(), _clock, CancellationToken.None);

        result.ShouldBeOfType<ProblemHttpResult>().StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        await _applications.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_permitted_change_whose_reread_is_null_falls_back_to_the_written_aggregate()
    {
        var appId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var app = App.Create(appId, jobId, T0, TransitionSource.Telegram);
        app.ChangeStatus(ApplicationStatus.Saved, TransitionSource.Telegram, T0.AddMinutes(1), ReminderPolicy.Default);
        _applications.FindByJobAsync(jobId, Arg.Any<CancellationToken>()).Returns(app);

        // The id resolves on the first read; the post-write re-read comes back null, so the Ok arm falls back to
        // the aggregate the handler already returned (the `?? application` branch).
        _history.HistoryAsync(appId, Arg.Any<CancellationToken>())
            .Returns(HistoryFor(appId, jobId, ApplicationStatus.Saved), (ApplicationHistory?)null);

        var result = await ApplicationEndpoints.HandleStatusChangeAsync(
            appId, new StatusChangeRequest("Interview", "first call scheduled"), _history, StatusHandler(),
            _clock, CancellationToken.None);

        result.ShouldBeOfType<Ok<ApplicationDetailResponse>>().Value!.Id.ShouldBe(appId);
        app.Status.ShouldBe(ApplicationStatus.Interview);
        await _applications.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_note_longer_than_the_cap_is_a_400_problem()
    {
        var appId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var app = App.Create(appId, jobId, T0, TransitionSource.Telegram);
        _history.HistoryAsync(appId, Arg.Any<CancellationToken>())
            .Returns(HistoryFor(appId, jobId, ApplicationStatus.Applied));
        _applications.FindByJobAsync(jobId, Arg.Any<CancellationToken>()).Returns(app);

        var tooLong = new string('x', ApplicationNote.MaxLength + 1);
        var result = await ApplicationEndpoints.HandleAddNoteAsync(
            appId, new AddNoteRequest(tooLong), _history, NoteHandler(), _clock, CancellationToken.None);

        result.ShouldBeOfType<ProblemHttpResult>().StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        await _applications.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_note_whose_aggregate_vanished_after_resolution_is_a_404()
    {
        var appId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        // The id resolves, but the write repository no longer tracks the job, so the note handler returns
        // ApplicationNotFound and the endpoint maps the default switch arm to a 404.
        _history.HistoryAsync(appId, Arg.Any<CancellationToken>())
            .Returns(HistoryFor(appId, jobId, ApplicationStatus.Applied));
        _applications.FindByJobAsync(jobId, Arg.Any<CancellationToken>()).Returns((App?)null);

        var result = await ApplicationEndpoints.HandleAddNoteAsync(
            appId, new AddNoteRequest("orphan note"), _history, NoteHandler(), _clock, CancellationToken.None);

        result.ShouldBeOfType<ProblemHttpResult>().StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        await _applications.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
