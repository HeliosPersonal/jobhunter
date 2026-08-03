using JobHunter.Domain.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JobHunter.Api.Endpoints;

/// <summary>
/// The job read endpoints (API contract §Jobs, T05): the full detail of one job, its provenance aliases,
/// and the cursor-paged recent-jobs list. All three declare <c>jobhunter:read</c> explicitly (the
/// endpoint-convention gate), read through Dapper/EF read seams only, and carry no CV-derived value, match
/// reason or application note (QG-2). The detail response includes the score slot F4's explainability
/// guarantee fills — null until F4 merges (the decoupling decision) — and never fabricates a ranking.
/// </summary>
public static class JobEndpoints
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    /// <summary>The lookback window for the recent-jobs list — a bounded scan, never the whole corpus.</summary>
    private static readonly TimeSpan RecentWindow = TimeSpan.FromDays(30);

    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/jobs", HandleListAsync)
            .WithName("ListJobs")
            .WithSummary("Recent live jobs, cursor-paged on (firstSeenAt, id).")
            .RequireAuthorization(ApiSecurityExtensions.ReadPolicy);

        app.MapGet("/api/jobs/{id:guid}", HandleDetailAsync)
            .WithName("JobDetail")
            .WithSummary("Full detail of one job, including its score components once ranking has run.")
            .RequireAuthorization(ApiSecurityExtensions.ReadPolicy);

        app.MapGet("/api/jobs/{id:guid}/aliases", HandleAliasesAsync)
            .WithName("JobAliases")
            .WithSummary("The raw postings that merged into this job, for inspecting a suspected bad merge.")
            .RequireAuthorization(ApiSecurityExtensions.ReadPolicy);

        return app;
    }

    internal static async Task<IResult> HandleDetailAsync(
        Guid id,
        IJobRepository jobs,
        ICompanyRepository companies,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(companies);

        var job = await jobs.FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return NotFound(id);
        }

        var company = await companies.FindAsync(job.CompanyId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(ResponseMapping.ToDetail(job, company));
    }

    internal static async Task<IResult> HandleAliasesAsync(
        Guid id,
        IJobRepository jobs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        var job = await jobs.FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return NotFound(id);
        }

        var aliases = job.Aliases.Select(ResponseMapping.ToAlias).ToList();
        return Results.Ok(aliases);
    }

    internal static async Task<IResult> HandleListAsync(
        HttpContext context,
        ILiveJobsQuery liveJobs,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(liveJobs);
        ArgumentNullException.ThrowIfNull(clock);

        var request = context.Request.Query;

        var limit = ParseLimit(request["limit"].ToString());

        // The keyset boundary: a cursor is a position past which to page. An invalid cursor is a 400,
        // never a silently wrong page; a cursor past the end simply matches nothing (an empty page).
        long? cursorFirstSeen = null;
        Guid cursorId = Guid.Empty;
        var cursor = request["cursor"].ToString();
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            if (!JobsCursor.TryDecode(cursor, out var firstSeen, out var lastId))
            {
                return Results.Problem(
                    type: SearchEndpoints.ErrorTypeBase + "invalid-cursor",
                    title: "The pagination cursor is not valid",
                    detail: "The pagination cursor is not valid.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            cursorFirstSeen = firstSeen;
            cursorId = lastId;
        }

        var since = clock.UtcNow - RecentWindow;
        var all = await liveJobs.DiscoveredSinceAsync(since, cancellationToken).ConfigureAwait(false);

        // The query returns newest-first; apply the keyset boundary and page in memory. The window is
        // bounded (30 days) so this is a small, ordered slice, not a corpus scan.
        var ordered = all
            .OrderByDescending(j => j.FirstSeenAt)
            .ThenByDescending(j => j.Id)
            .ToList();

        IEnumerable<Domain.Jobs.LiveJob> page = ordered;
        if (cursorFirstSeen is { } boundary)
        {
            var boundaryInstant = DateTimeOffset.FromUnixTimeSeconds(boundary);
            page = ordered.Where(j =>
                j.FirstSeenAt < boundaryInstant
                || (j.FirstSeenAt == boundaryInstant && j.Id.CompareTo(cursorId) < 0));
        }

        var taken = page.Take(limit + 1).ToList();
        var hasMore = taken.Count > limit;
        var window = taken.Take(limit).ToList();

        string? nextCursor = null;
        if (hasMore && window.Count > 0)
        {
            var last = window[^1];
            nextCursor = JobsCursor.Encode(last.FirstSeenAt.ToUnixTimeSeconds(), last.Id);
        }

        var response = new JobsListResponse(
            [.. window.Select(ResponseMapping.ToSummary)],
            nextCursor);

        return Results.Ok(response);
    }

    private static int ParseLimit(string raw) =>
        int.TryParse(raw, out var value) && value > 0
            ? Math.Min(value, MaxPageSize)
            : DefaultPageSize;

    private static IResult NotFound(Guid id) => Results.Problem(
        type: SearchEndpoints.ErrorTypeBase + "not-found",
        title: "The requested job does not exist",
        detail: $"No job was found with id {id}.",
        statusCode: StatusCodes.Status404NotFound);
}
