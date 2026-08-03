using System.Diagnostics.CodeAnalysis;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Scalar.AspNetCore;

namespace JobHunter.Api;

/// <summary>
/// The non-auth host pipeline for the API surface (T04): RFC 7807 problem details that never leak an
/// internal detail, OpenAPI generation rendered by Scalar, and a per-token rate limiter layered behind
/// Cloudflare's edge limiting. Host composition — excluded from coverage, exercised by the API's
/// integration tests.
/// </summary>
[ExcludeFromCodeCoverage]
public static class ApiPipelineExtensions
{
    /// <summary>The name of the per-token fixed-window rate-limiting policy.</summary>
    public const string PerTokenPolicy = "per-token";

    private const int PermitLimit = 60;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static WebApplicationBuilder AddApiPipeline(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // RFC 7807 for every failed request. CustomizeProblemDetails keeps the body to the standard
        // fields plus a correlating traceId — no exception message, no stack, no internal detail.
        builder.Services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            });

        // .NET built-in OpenAPI document; served raw at /openapi/v1.json and rendered by Scalar.
        builder.Services.AddOpenApi();

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Applied per token across the whole surface (AC), layered behind Cloudflare's edge limiting.
            // A fixed window keyed on the bearer token, so the single Owner's limit is not shared with
            // another caller behind the same Cloudflare egress IP.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitPartitionKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = PermitLimit,
                        Window = Window,
                        QueueLimit = 0,
                    }));

            // A named policy of the same shape, for an endpoint that wants a tighter, opt-in bucket.
            options.AddPolicy(PerTokenPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitPartitionKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = PermitLimit,
                        Window = Window,
                        QueueLimit = 0,
                    }));
        });

        return builder;
    }

    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Turns any unhandled fault into an RFC 7807 response rather than a leaked stack trace.
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.UseRateLimiter();

        app.MapOpenApi();
        app.MapScalarApiReference();

        return app;
    }

    // Partition by the caller's bearer token so the limit is per token, not per source IP (a single
    // Owner behind one Cloudflare egress would otherwise share one bucket). Unauthenticated calls fall
    // back to the connection's remote IP.
    private static string RateLimitPartitionKey(HttpContext httpContext)
    {
        var authorization = httpContext.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authorization))
        {
            return authorization;
        }

        return httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
    }
}
