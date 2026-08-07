using JobHunter.Application.Preferences;
using JobHunter.Domain.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JobHunter.Api.Endpoints;

/// <summary>
/// The F7 preference-learning endpoints (T08 C6, AC-10): the two reads the Owner inspects the learned model
/// through — its weights, each with the one-sentence explanation that makes it questionable (AC-03), and the
/// latest Run's hidden jobs with the reason each was withheld (risk D3) — and the three owner overrides: disable
/// a single weight (AC-06), reset the whole model (done-when 3) and toggle learning on or off (AC-07). The two
/// reads declare <c>jobhunter:read</c> and the three writes <c>jobhunter:admin</c> explicitly (the
/// endpoint-convention gate); the F0 fallback-deny policy refuses any route added without one, and the
/// scope-plus-Owner assertion means a valid token for any other subject is a 403.
///
/// <para>Every write delegates to the same Application handler the Telegram override command uses, so the two
/// surfaces share one write path (there is no second implementation of "disable a weight"). An unknown weight
/// and a reset with nothing active answer 404 as problem+json — expected outcomes the caller renders, not
/// faults. No response carries a CV-derived value or a match reason (the CV crosses exactly one boundary, and it
/// is not this one).</para>
/// </summary>
public static class PreferenceEndpoints
{
    public static IEndpointRouteBuilder MapPreferenceEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/preferences/weights", HandleWeightsAsync)
            .WithName("PreferenceWeights")
            .WithSummary("The active model's learned weights, each with the one-sentence explanation of why it exists.")
            .Produces<IReadOnlyList<LearnedWeightResponse>>()
            .RequireAuthorization(ApiSecurityExtensions.ReadPolicy);

        app.MapGet("/api/preferences/hidden", HandleHiddenAsync)
            .WithName("PreferenceHidden")
            .WithSummary("The latest run's suppressed jobs with the reason each was withheld, so suppression regret is measurable.")
            .Produces<IReadOnlyList<HiddenJobResponse>>()
            .RequireAuthorization(ApiSecurityExtensions.ReadPolicy);

        app.MapPost("/api/preferences/weights/{weightId:guid}/disable", HandleDisableAsync)
            .WithName("DisablePreferenceWeight")
            .WithSummary("Switch a single learned weight off; it stops affecting the next ranking and stays inspectable.")
            .Produces<DisableWeightResponse>()
            .RequireAuthorization(ApiSecurityExtensions.AdminPolicy);

        app.MapPost("/api/preferences/reset", HandleResetAsync)
            .WithName("ResetPreferenceModel")
            .WithSummary("Deactivate the active model wholesale; no signal is deleted, so a future refit can rebuild it.")
            .Produces<ResetModelResponse>()
            .RequireAuthorization(ApiSecurityExtensions.AdminPolicy);

        app.MapPut("/api/preferences/learning", HandleSetLearningAsync)
            .WithName("SetPreferenceLearning")
            .WithSummary("Turn learning on or off; off applies only explicit preferences and is stated on the next digest.")
            .Produces<LearningStateResponse>()
            .RequireAuthorization(ApiSecurityExtensions.AdminPolicy);

        return app;
    }

    internal static async Task<IResult> HandleWeightsAsync(
        ActiveWeightsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var weights = await query.ActiveAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(weights
            .Select(w => new LearnedWeightResponse(
                w.WeightId, w.Dimension.ToString(), w.Value, w.Weight, w.Disabled, w.Explanation))
            .ToList());
    }

    internal static async Task<IResult> HandleHiddenAsync(
        IHiddenJobsQuery hidden,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hidden);

        // A generous cap keeps a wide day's message bounded without paging on a single-owner surface.
        var jobs = await hidden.HiddenAsync(limit: 50, cancellationToken).ConfigureAwait(false);
        return Results.Ok(jobs
            .Select(j => new HiddenJobResponse(j.JobId, j.Title, j.Company, j.Score, j.SuppressionReason))
            .ToList());
    }

    internal static async Task<IResult> HandleDisableAsync(
        Guid weightId,
        DisablePreferenceWeightHandler handler,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(clock);

        var outcome = await handler
            .Handle(new DisablePreferenceWeightCommand(weightId, clock.UtcNow), cancellationToken)
            .ConfigureAwait(false);

        return outcome.Result switch
        {
            DisablePreferenceWeightResult.Disabled =>
                Results.Ok(new DisableWeightResponse(weightId, outcome.Explanation!)),

            // No active model carries a weight with that id — a 404 the caller renders, not a fault.
            _ => Results.Problem(
                type: SearchEndpoints.ErrorTypeBase + "weight-not-found",
                title: "The requested preference weight does not exist",
                detail: $"No active model carries a weight with id {weightId}.",
                statusCode: StatusCodes.Status404NotFound),
        };
    }

    internal static async Task<IResult> HandleResetAsync(
        ResetPreferenceModelHandler handler,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(clock);

        var outcome = await handler
            .Handle(new ResetPreferenceModelCommand(clock.UtcNow), cancellationToken)
            .ConfigureAwait(false);

        return outcome.Result switch
        {
            ResetPreferenceModelResult.Reset =>
                Results.Ok(new ResetModelResponse(outcome.DeactivatedVersion!.Value)),

            // Nothing was active — a 404 the caller renders, not a fault.
            _ => Results.Problem(
                type: SearchEndpoints.ErrorTypeBase + "no-active-model",
                title: "There is no active preference model to reset",
                detail: "No preference model is currently active.",
                statusCode: StatusCodes.Status404NotFound),
        };
    }

    internal static async Task<IResult> HandleSetLearningAsync(
        SetLearningRequest? request,
        SetLearningEnabledHandler handler,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(clock);

        // A missing body is a client error the caller fixes — a 400, never a silent default that flips learning.
        if (request is null)
        {
            return Results.Problem(
                type: SearchEndpoints.ErrorTypeBase + "learning-state-missing",
                title: "The learning state is required",
                detail: "The request body must state whether learning should be enabled.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var outcome = await handler
            .Handle(new SetLearningEnabledCommand(request.Enabled, clock.UtcNow), cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new LearningStateResponse(outcome.Enabled, outcome.Changed));
    }
}
