using JobHunter.Domain.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JobHunter.Api.Endpoints;

/// <summary>
/// The public search endpoint (API contract §Search, T05). It delegates to the one
/// <see cref="ISearchQuery"/> port — the same read path the Telegram <c>/search</c> command uses, so
/// there is no second search implementation — and translates its <see cref="Domain.Common.Result{T}"/>:
/// an unreachable index is a plain 503 with the rest of the system unaffected (AC-09, QG-3), and an
/// invalid cursor is a 400, never a wrong page. The endpoint declares <c>jobhunter:read</c> explicitly so
/// the endpoint-convention suite (T10) sees a scope on every route (AC on §Search).
/// </summary>
public static class SearchEndpoints
{
    /// <summary>The type URI stem for the RFC 7807 problem documents this feature emits.</summary>
    internal const string ErrorTypeBase = "https://jobhunter.devoverflow.org/errors/";

    private const string CursorInvalidCode = "search.cursor.invalid";

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/search", HandleSearchAsync)
            .WithName("Search")
            .WithSummary("Full-text job search with typed filters and facet counts.")
            .RequireAuthorization(ApiSecurityExtensions.ReadPolicy);

        return app;
    }

    internal static async Task<IResult> HandleSearchAsync(
        HttpContext context,
        ISearchQuery search,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(search);

        var query = SearchQueryBinding.FromQuery(context.Request.Query);
        var result = await search.SearchAsync(query, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return Results.Ok(ResponseMapping.ToResponse(result.Value));
        }

        // An invalid cursor is the caller's fault (400); anything else is an unavailable index (503),
        // stated plainly so the client knows the rest of the system is unaffected (AC-09).
        if (string.Equals(result.Error.Code, CursorInvalidCode, StringComparison.Ordinal))
        {
            return Results.Problem(
                type: ErrorTypeBase + "invalid-cursor",
                title: "The pagination cursor is not valid",
                detail: result.Error.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Problem(
            type: ErrorTypeBase + "search-unavailable",
            title: "Search is temporarily unavailable",
            detail: "The search index could not be reached. Other functionality is unaffected.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
