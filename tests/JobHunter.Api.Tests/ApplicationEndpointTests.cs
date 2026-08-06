using System.Net;
using JobHunter.Api.Endpoints;
using JobHunter.Domain.Applications;
using JobHunter.Domain.Preferences;
using NSubstitute;
using Shouldly;
using Xunit;
using App = JobHunter.Domain.Applications.Application;

namespace JobHunter.Api.Tests;

/// <summary>
/// The F6 application-tracking endpoints end-to-end (T09): the pipeline grouped by status (AC-01), one
/// application with its full history and notes (AC-03), the two admin writes — a status change (AC-10) and a
/// note (AC-06) — and the what-needs-attention read (T06). The two reads declare <c>jobhunter:read</c> and the
/// two writes <c>jobhunter:admin</c>; a read token on a write is a 403 and an anonymous call a 401 (the
/// endpoint-convention gate). A refused transition answers 409 naming the rule <em>and</em> the remedy, an
/// unknown application 404 as problem+json, and a change through the API records <see cref="TransitionSource.Api"/>
/// — distinguishable from a Telegram one (done-when 4). No response carries a CV-derived value or a match reason.
/// </summary>
public sealed class ApplicationEndpointTests : IClassFixture<EndpointsHostFactory>
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 6, 7, 0, 0, TimeSpan.Zero);

    private readonly EndpointsHostFactory _factory;

    public ApplicationEndpointTests(EndpointsHostFactory factory) => _factory = factory;

    // --- Pipeline (read) ---------------------------------------------------------------------------

    [Fact]
    public async Task Pipeline_returns_the_groups_and_their_counts()
    {
        var appId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        _factory.ApplicationPipeline.PipelineAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ApplicationPipeline(
            [
                new PipelineGroup(ApplicationStatus.Interview,
                [
                    new PipelineEntry(appId, jobId, "Staff Backend Engineer", "Snowflake", 95m,
                        PostingClosed: false, AppliedAt: T0, LastActivityAt: T0.AddDays(1),
                        NextActionAt: T0.AddDays(3), DaysInStage: 5),
                ]),
            ]));

        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri("/api/applications", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApplicationPipelineResponse>();
        body.ShouldNotBeNull();
        body.Counts["Interview"].ShouldBe(1);
        var group = body.Groups.ShouldHaveSingleItem();
        group.Status.ShouldBe("Interview");
        var entry = group.Applications.ShouldHaveSingleItem();
        entry.Id.ShouldBe(appId);
        entry.JobId.ShouldBe(jobId);
        entry.Title.ShouldBe("Staff Backend Engineer");
        entry.Company.ShouldBe("Snowflake");
        entry.Score.ShouldBe(95m);
        entry.DaysInStage.ShouldBe(5);
        entry.NextActionAt.ShouldBe(T0.AddDays(3).ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Pipeline_without_a_token_is_a_401()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/api/applications", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // --- Detail (read) -----------------------------------------------------------------------------

    [Fact]
    public async Task Detail_returns_the_application_with_its_history_and_notes()
    {
        var appId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        _factory.ApplicationHistory.HistoryAsync(appId, Arg.Any<CancellationToken>())
            .Returns(new ApplicationHistory(appId, jobId, "Staff Backend Engineer", "Snowflake",
                ApplicationStatus.Interview, PostingClosed: false, Archived: false,
                AppliedAt: T0, LastActivityAt: T0.AddDays(1), NextActionAt: T0.AddDays(3),
                Transitions:
                [
                    new HistoryTransition(null, ApplicationStatus.New, TransitionSource.Telegram, null, T0),
                    new HistoryTransition(ApplicationStatus.New, ApplicationStatus.Interview,
                        TransitionSource.Api, "first call scheduled", T0.AddDays(1)),
                ],
                Notes: [new HistoryNote("call went well", T0.AddDays(1))]));

        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri($"/api/applications/{appId}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApplicationDetailResponse>();
        body.ShouldNotBeNull();
        body.Id.ShouldBe(appId);
        body.JobId.ShouldBe(jobId);
        body.Status.ShouldBe("Interview");
        body.Transitions.Count.ShouldBe(2);
        body.Transitions[1].From.ShouldBe("New");
        body.Transitions[1].To.ShouldBe("Interview");
        body.Transitions[1].Source.ShouldBe("Api");
        body.Transitions[1].Detail.ShouldBe("first call scheduled");
        body.Notes.ShouldHaveSingleItem().Body.ShouldBe("call went well");
    }

    [Fact]
    public async Task Detail_of_an_unknown_application_is_a_404()
    {
        var appId = Guid.NewGuid();
        _factory.ApplicationHistory.HistoryAsync(appId, Arg.Any<CancellationToken>())
            .Returns((ApplicationHistory?)null);

        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri($"/api/applications/{appId}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    // --- Due (read) --------------------------------------------------------------------------------

    [Fact]
    public async Task Due_returns_what_needs_attention_now()
    {
        var appId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        _factory.DueReminders.DueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<DueReminder>
            {
                new(appId, jobId, "Staff Backend Engineer", "Snowflake", "https://apply.example/1",
                    ApplicationStatus.Applied, PostingClosed: false, LastReminderCondition: null),
            });

        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri("/api/applications/due", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<DueReminderResponse>>();
        body.ShouldNotBeNull();
        var reminder = body.ShouldHaveSingleItem();
        reminder.ApplicationId.ShouldBe(appId);
        reminder.JobId.ShouldBe(jobId);
        reminder.ApplyUrl.ShouldBe("https://apply.example/1");
        reminder.Status.ShouldBe("Applied");
    }

    // --- Status change (admin) ---------------------------------------------------------------------

    [Fact]
    public async Task Status_change_permitted_returns_200_and_records_the_api_source()
    {
        var jobId = Guid.NewGuid();
        var app = Seed(jobId, ApplicationStatus.Saved, out var appId);
        _factory.JobFacts.SnapshotAsync(jobId, Arg.Any<CancellationToken>())
            .Returns(JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>> { [Dimension.Country] = ["DE"] }));
        // The write substitute is shared across the fixture; count only this test's calls.
        _factory.Applications.ClearReceivedCalls();

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsJsonAsync(
            new Uri($"/api/applications/{appId}/status", UriKind.Relative),
            new StatusChangeRequest("Interview", "first call scheduled"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        app.Status.ShouldBe(ApplicationStatus.Interview);
        // done-when 4: a change through the API records Api, distinguishable from a Telegram change.
        app.Transitions[^1].Source.ShouldBe(TransitionSource.Api);
        app.Transitions[^1].Detail.ShouldBe("first call scheduled");
        await _factory.Applications.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Status_change_refused_returns_409_naming_the_rule_and_the_remedy()
    {
        var jobId = Guid.NewGuid();
        Seed(jobId, ApplicationStatus.Rejected, out var appId);
        // The write substitute is shared across the fixture; assert only this test made no call.
        _factory.Applications.ClearReceivedCalls();

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsJsonAsync(
            new Uri($"/api/applications/{appId}/status", UriKind.Relative),
            new StatusChangeRequest("Interview", null));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var raw = await response.Content.ReadAsStringAsync();
        // AC-10 / done-when 2: the body names the attempted transition and the remedy, not just the refusal.
        raw.ShouldContain("Rejected");
        raw.ShouldContain("Interview");
        raw.ShouldContain("cannot return to Interview after Rejected");
        await _factory.Applications.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Status_change_for_an_unknown_application_is_a_404()
    {
        var appId = Guid.NewGuid();
        _factory.ApplicationHistory.HistoryAsync(appId, Arg.Any<CancellationToken>())
            .Returns((ApplicationHistory?)null);

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsJsonAsync(
            new Uri($"/api/applications/{appId}/status", UriKind.Relative),
            new StatusChangeRequest("Interview", null));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Status_change_with_an_unrecognised_target_is_a_400()
    {
        var appId = Guid.NewGuid();

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsJsonAsync(
            new Uri($"/api/applications/{appId}/status", UriKind.Relative),
            new StatusChangeRequest("Nonsense", null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Status_change_with_only_a_read_token_is_a_403()
    {
        using var client = _factory.OwnerClient();
        var response = await client.PostAsJsonAsync(
            new Uri($"/api/applications/{Guid.NewGuid()}/status", UriKind.Relative),
            new StatusChangeRequest("Interview", null));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Status_change_without_a_token_is_a_401()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            new Uri($"/api/applications/{Guid.NewGuid()}/status", UriKind.Relative),
            new StatusChangeRequest("Interview", null));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // --- Notes (admin) -----------------------------------------------------------------------------

    [Fact]
    public async Task Note_recorded_returns_200()
    {
        var jobId = Guid.NewGuid();
        Seed(jobId, ApplicationStatus.Applied, out var appId);
        // The write substitute is shared across the fixture; count only this test's calls.
        _factory.Applications.ClearReceivedCalls();

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsJsonAsync(
            new Uri($"/api/applications/{appId}/notes", UriKind.Relative),
            new AddNoteRequest("phone screen scheduled"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await _factory.Applications.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Note_with_a_blank_body_is_a_400()
    {
        var jobId = Guid.NewGuid();
        Seed(jobId, ApplicationStatus.Applied, out var appId);

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsJsonAsync(
            new Uri($"/api/applications/{appId}/notes", UriKind.Relative),
            new AddNoteRequest("   "));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Note_for_an_unknown_application_is_a_404()
    {
        var appId = Guid.NewGuid();
        _factory.ApplicationHistory.HistoryAsync(appId, Arg.Any<CancellationToken>())
            .Returns((ApplicationHistory?)null);

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsJsonAsync(
            new Uri($"/api/applications/{appId}/notes", UriKind.Relative),
            new AddNoteRequest("orphan note"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Note_with_only_a_read_token_is_a_403()
    {
        using var client = _factory.OwnerClient();
        var response = await client.PostAsJsonAsync(
            new Uri($"/api/applications/{Guid.NewGuid()}/notes", UriKind.Relative),
            new AddNoteRequest("nope"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Seeds a real application at <paramref name="status"/> behind both the id→job bridge
    /// (<see cref="IApplicationHistoryQuery"/>) and the write repository, so an admin write drives the real
    /// aggregate through substituted collaborators. Returns the aggregate so a test can assert on its transitions.
    /// </summary>
    private App Seed(Guid jobId, ApplicationStatus status, out Guid appId)
    {
        appId = Guid.NewGuid();
        var app = App.Create(appId, jobId, T0, TransitionSource.Telegram);
        if (status != ApplicationStatus.New)
        {
            app.ChangeStatus(status, TransitionSource.Telegram, T0.AddMinutes(1), ReminderPolicy.Default);
        }

        _factory.Applications.FindByJobAsync(jobId, Arg.Any<CancellationToken>()).Returns(app);
        _factory.ApplicationHistory.HistoryAsync(appId, Arg.Any<CancellationToken>())
            .Returns(new ApplicationHistory(appId, jobId, "Staff Backend Engineer", "Snowflake",
                status, PostingClosed: false, Archived: false,
                AppliedAt: null, LastActivityAt: T0, NextActionAt: null,
                Transitions: [], Notes: []));
        return app;
    }
}
