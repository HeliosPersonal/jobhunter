using JobHunter.Application.Applications;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JobHunter.Api.Endpoints;

/// <summary>
/// The F6 application-tracking endpoints (contract [[application-api]], T09): the pipeline grouped by status
/// (AC-01), one application with its full history and notes (AC-03), the two owner writes — a status change
/// (AC-10) and a note (AC-06) — and the what-needs-attention read (T06). The two reads declare
/// <c>jobhunter:read</c> and the two writes <c>jobhunter:admin</c> explicitly (the endpoint-convention gate);
/// the F0 fallback-deny policy refuses any route added without one.
///
/// <para>The API addresses an application by <c>id</c>, but every F6 write path is job-keyed (QG-1), so the two
/// writes first resolve the id to its job through <see cref="IApplicationHistoryQuery"/> — a null is a 404 —
/// before dispatching a job-keyed command. A refused transition answers 409 with a body that names the rule
/// <em>and</em> the remedy (AC-10), never a bare refusal; an unrecognised target status is a 400. A change made
/// here records <see cref="TransitionSource.Api"/>, which is what makes it distinguishable from a Telegram one
/// in the history (done-when 4). No response carries a CV-derived value or a match reason (the CV crosses
/// exactly one boundary, and it is not this one).</para>
/// </summary>
public static class ApplicationEndpoints
{
    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/applications", HandlePipelineAsync)
            .WithName("ApplicationPipeline")
            .WithSummary("The application pipeline, grouped by status, with per-status counts.")
            .RequireAuthorization(ApiSecurityExtensions.ReadPolicy);

        app.MapGet("/api/applications/due", HandleDueAsync)
            .WithName("ApplicationsDue")
            .WithSummary("The applications whose next action is due now — what needs attention.")
            .RequireAuthorization(ApiSecurityExtensions.ReadPolicy);

        app.MapGet("/api/applications/{id:guid}", HandleDetailAsync)
            .WithName("ApplicationDetail")
            .WithSummary("One application with its complete transition history and its notes.")
            .RequireAuthorization(ApiSecurityExtensions.ReadPolicy);

        app.MapPost("/api/applications/{id:guid}/status", HandleStatusChangeAsync)
            .WithName("ChangeApplicationStatus")
            .WithSummary("Move an application to a new status; a refused transition names the rule and the remedy.")
            .RequireAuthorization(ApiSecurityExtensions.AdminPolicy);

        app.MapPost("/api/applications/{id:guid}/notes", HandleAddNoteAsync)
            .WithName("AddApplicationNote")
            .WithSummary("Attach a free-text note to an application; the note is activity, never a status change.")
            .RequireAuthorization(ApiSecurityExtensions.AdminPolicy);

        return app;
    }

    internal static async Task<IResult> HandlePipelineAsync(
        IApplicationPipelineQuery pipeline,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(clock);

        var view = await pipeline.PipelineAsync(clock.UtcNow, cancellationToken).ConfigureAwait(false);
        return Results.Ok(ResponseMapping.ToPipeline(view));
    }

    internal static async Task<IResult> HandleDueAsync(
        IDueReminderQuery due,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(due);
        ArgumentNullException.ThrowIfNull(clock);

        var reminders = await due.DueAsync(clock.UtcNow, cancellationToken).ConfigureAwait(false);
        return Results.Ok(reminders.Select(ResponseMapping.ToDueReminder).ToList());
    }

    internal static async Task<IResult> HandleDetailAsync(
        Guid id,
        IApplicationHistoryQuery history,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(history);

        var application = await history.HistoryAsync(id, cancellationToken).ConfigureAwait(false);
        if (application is null)
        {
            return NotFound(id);
        }

        return Results.Ok(ResponseMapping.ToApplicationDetail(application));
    }

    internal static async Task<IResult> HandleStatusChangeAsync(
        Guid id,
        StatusChangeRequest? request,
        IApplicationHistoryQuery history,
        ChangeApplicationStatusHandler handler,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(clock);

        // An unrecognised target status is a client error the caller fixes, not a refused transition — a 400.
        if (request is null || !Enum.TryParse<ApplicationStatus>(request.ToStatus, ignoreCase: false, out var toStatus))
        {
            return Results.Problem(
                type: SearchEndpoints.ErrorTypeBase + "invalid-status",
                title: "The target status is not a recognised application status",
                detail: $"'{request?.ToStatus}' is not one of the seven application statuses.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The API addresses an application by id; every F6 write is job-keyed (QG-1). Resolve id -> job first;
        // an unknown id is a 404 before any command runs.
        var application = await history.HistoryAsync(id, cancellationToken).ConfigureAwait(false);
        if (application is null)
        {
            return NotFound(id);
        }

        var command = new ChangeApplicationStatusCommand(
            application.JobId, toStatus, TransitionSource.Api, clock.UtcNow, request.Detail);
        var outcome = await handler.Handle(command, cancellationToken).ConfigureAwait(false);

        return outcome.Result switch
        {
            // The application vanished between the id resolution and the write (it is not tracked) — a 404.
            ChangeApplicationStatusResult.ApplicationNotFound => NotFound(id),

            // AC-10: the 409 states the rule, not just the refusal — the attempted move and the remedy.
            ChangeApplicationStatusResult.NotPermitted => Results.Json(
                new TransitionNotPermittedResponse(
                    "TransitionNotPermitted",
                    outcome.From!.Value.ToString(),
                    outcome.To.ToString(),
                    outcome.Remedy!),
                statusCode: StatusCodes.Status409Conflict,
                contentType: "application/problem+json"),

            _ => Results.Ok(ResponseMapping.ToApplicationDetail(
                await history.HistoryAsync(id, cancellationToken).ConfigureAwait(false) ?? application)),
        };
    }

    internal static async Task<IResult> HandleAddNoteAsync(
        Guid id,
        AddNoteRequest? request,
        IApplicationHistoryQuery history,
        AddNoteHandler handler,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(clock);

        // Resolve id -> job (QG-1); an unknown id is a 404 before the note command runs.
        var application = await history.HistoryAsync(id, cancellationToken).ConfigureAwait(false);
        if (application is null)
        {
            return NotFound(id);
        }

        var outcome = await handler
            .Handle(new AddNoteCommand(application.JobId, request?.Body ?? string.Empty, clock.UtcNow), cancellationToken)
            .ConfigureAwait(false);

        return outcome switch
        {
            AddNoteOutcome.Recorded => Results.Ok(),

            // A blank or over-long body is a client error the caller fixes — a 400 naming which.
            AddNoteOutcome.Empty => Results.Problem(
                type: SearchEndpoints.ErrorTypeBase + "note-empty",
                title: "The note body is empty",
                detail: "A note must contain some text.",
                statusCode: StatusCodes.Status400BadRequest),
            AddNoteOutcome.TooLong => Results.Problem(
                type: SearchEndpoints.ErrorTypeBase + "note-too-long",
                title: "The note body is too long",
                detail: $"A note may be at most {ApplicationNote.MaxLength} characters.",
                statusCode: StatusCodes.Status400BadRequest),

            // The application vanished between the id resolution and the write — a 404.
            _ => NotFound(id),
        };
    }

    private static IResult NotFound(Guid id) => Results.Problem(
        type: SearchEndpoints.ErrorTypeBase + "not-found",
        title: "The requested application does not exist",
        detail: $"No application was found with id {id}.",
        statusCode: StatusCodes.Status404NotFound);
}
