using JobHunter.Application.Search;
using JobHunter.Domain.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JobHunter.Api.Endpoints;

/// <summary>
/// The operational endpoints (API contract §Operations, T07): every runbook recovery action as an
/// endpoint so recovery never requires database access (AC-07, US-06) — a full index rebuild (R8), the
/// release of a quarantined source (R4), a history reprocess (F2 AC-09) and a corpus-stats snapshot. All
/// four declare <c>jobhunter:admin</c> explicitly (the endpoint-convention gate); with only the read
/// scope they are refused with a 403 (AC-07). The two long-running actions — reindex and reprocess — are
/// enqueued through <see cref="IOperationScheduler"/> and return an operation id immediately rather than
/// blocking the request thread; the source release and the stats read are synchronous.
///
/// <para>The run-lifecycle recovery actions the runbooks also list (resume, redeliver) are F5-owned and
/// deferred until F5 merges (the cross-feature decoupling decision) — modelling them here would take a
/// dependency on an unmerged feature.</para>
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/api/admin/search/reindex", HandleReindexAsync)
            .WithName("Reindex")
            .WithSummary("Enqueues a full search-index rebuild and returns its operation id (runbook R8).")
            .RequireAuthorization(ApiSecurityExtensions.AdminPolicy);

        app.MapPost("/api/admin/sources/{id:guid}/unquarantine", HandleUnquarantineAsync)
            .WithName("UnquarantineSource")
            .WithSummary("Releases a quarantined source so the next discovery cycle fetches it (runbook R4).")
            .RequireAuthorization(ApiSecurityExtensions.AdminPolicy);

        app.MapPost("/api/admin/jobs/reprocess", HandleReprocessAsync)
            .WithName("ReprocessJobs")
            .WithSummary("Enqueues a re-normalisation of jobs first seen in a window and returns its operation id.")
            .RequireAuthorization(ApiSecurityExtensions.AdminPolicy);

        app.MapGet("/api/admin/stats", HandleStatsAsync)
            .WithName("CorpusStats")
            .WithSummary("Corpus counts and the search-index drift the nightly reconcile acts on.")
            .RequireAuthorization(ApiSecurityExtensions.AdminPolicy);

        return app;
    }

    internal static IResult HandleReindex(IOperationScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        var operationId = scheduler.EnqueueReindex();
        return Results.Accepted(
            $"/api/admin/operations/{operationId}",
            new OperationAcceptedResponse(operationId, "Enqueued"));
    }

    internal static Task<IResult> HandleReindexAsync(IOperationScheduler scheduler) =>
        Task.FromResult(HandleReindex(scheduler));

    internal static async Task<IResult> HandleUnquarantineAsync(
        Guid id,
        SourceQuarantineService sources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var result = await sources.UnquarantineAsync(id, cancellationToken).ConfigureAwait(false);

        // The service reports outcomes as values (QG-3): an unknown id is a 404, an already-healthy source
        // is a 200 that says "nothing to do", and a genuine release is a 200.
        return result.Value switch
        {
            ReleaseOutcome.NotFound => Results.Problem(
                type: SearchEndpoints.ErrorTypeBase + "not-found",
                title: "The requested source does not exist",
                detail: $"No source was found with id {id}.",
                statusCode: StatusCodes.Status404NotFound),
            _ => Results.Ok(new UnquarantineResponse(id, result.Value.ToString())),
        };
    }

    internal static IResult HandleReprocess(ReprocessRequest? request, IOperationScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        // Absent lower bound reprocesses the full history, matching the reprocess CLI verb's default.
        var firstSeenFrom = request?.FirstSeenFrom ?? DateTimeOffset.MinValue;
        var operationId = scheduler.EnqueueReprocess(firstSeenFrom);
        return Results.Accepted(
            $"/api/admin/operations/{operationId}",
            new OperationAcceptedResponse(operationId, "Enqueued"));
    }

    internal static Task<IResult> HandleReprocessAsync(ReprocessRequest? request, IOperationScheduler scheduler) =>
        Task.FromResult(HandleReprocess(request, scheduler));

    internal static async Task<IResult> HandleStatsAsync(
        CorpusStatsService stats,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stats);

        var snapshot = await stats.CollectAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(new StatsResponse(
            snapshot.LiveJobs, snapshot.IndexedDocuments, snapshot.Drift, snapshot.IndexAvailable));
    }
}
