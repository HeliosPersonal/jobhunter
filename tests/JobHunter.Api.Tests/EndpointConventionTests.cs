using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace JobHunter.Api.Tests;

/// <summary>
/// The endpoint-convention suite (F9-T10, AC-05, AC-06, gate G7): the test that turns "someone adds an
/// endpoint and forgets the scope" from a security finding into a build failure. It reflects over the
/// host's real registered endpoints and asserts every business endpoint (<c>/api/*</c> and the
/// admin-scoped <c>/health</c>) declares an authorisation policy, that the two probes (<c>/alive</c>,
/// <c>/ready</c>) are deliberately anonymous, and that the generated OpenAPI document describes every
/// registered <c>/api</c> endpoint with a documented response. The fallback-deny policy makes an
/// unscoped endpoint fail closed at runtime; this makes it fail at build time, which is better — and the
/// deliberately-unprotected fixture endpoint proves the check can actually fail.
/// </summary>
public sealed partial class EndpointConventionTests : IClassFixture<EndpointsHostFactory>
{
    private readonly EndpointsHostFactory _factory;

    public EndpointConventionTests(EndpointsHostFactory factory) => _factory = factory;

    // --- The convention, as a pure predicate over endpoints -----------------------------------------

    /// <summary>The two probe routes that are deliberately anonymous (observability §3); nothing else is.</summary>
    private static readonly HashSet<string> AnonymousProbes = new(StringComparer.Ordinal) { "/alive", "/ready" };

    /// <summary>
    /// Returns the endpoints that violate the convention: a business endpoint (<c>/api/*</c> or
    /// <c>/health</c>) that declares no authorisation policy, or a probe that mistakenly declares one.
    /// Framework documentation endpoints (OpenAPI, Scalar) are outside the rule — they carry no business
    /// data and are covered by the host's fallback-deny policy.
    /// </summary>
    internal static IReadOnlyList<string> ConventionViolations(IEnumerable<Endpoint> endpoints)
    {
        var violations = new List<string>();
        foreach (var endpoint in endpoints.OfType<RouteEndpoint>())
        {
            var route = "/" + endpoint.RoutePattern.RawText?.TrimStart('/');
            var hasPolicy = endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null;

            if (AnonymousProbes.Contains(route))
            {
                if (hasPolicy)
                {
                    violations.Add($"{route} is a probe but declares an authorisation policy");
                }

                continue;
            }

            if ((route.StartsWith("/api/", StringComparison.Ordinal) || route == "/health") && !hasPolicy)
            {
                violations.Add($"{route} declares no authorisation policy");
            }
        }

        return violations;
    }

    // --- The real host obeys the convention (AC-06, gate G7) ----------------------------------------

    [Fact]
    public void Every_business_endpoint_declares_a_scope_and_the_probes_are_anonymous()
    {
        var endpoints = HostEndpoints();

        // Sanity: the host really did map the F9 surface, so an empty registry cannot pass this vacuously.
        endpoints.OfType<RouteEndpoint>()
            .Select(e => "/" + e.RoutePattern.RawText?.TrimStart('/'))
            .ShouldContain("/api/search");

        ConventionViolations(endpoints).ShouldBeEmpty();
    }

    // --- The check can actually fail: a deliberately unprotected endpoint (proving it can) ----------

    [Fact]
    public void A_deliberately_unprotected_api_endpoint_is_flagged_as_a_violation()
    {
        // The fixture the DoD calls for: an endpoint added without RequireAuthorization. The convention
        // predicate must catch it — otherwise the passing real-host assertion above would prove nothing.
        var unprotected = RouteEndpointFor("/api/leak", authorize: false);
        var protectedPeer = RouteEndpointFor("/api/safe", authorize: true);

        var violations = ConventionViolations([unprotected, protectedPeer]);

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("/api/leak");
    }

    [Fact]
    public void A_probe_that_mistakenly_declares_a_policy_is_also_flagged()
    {
        var overProtectedProbe = RouteEndpointFor("/alive", authorize: true);

        var violations = ConventionViolations([overProtectedProbe]);

        violations.ShouldHaveSingleItem();
        violations[0].ShouldContain("/alive");
    }

    // --- OpenAPI describes every registered /api endpoint with a documented response (AC-05) ---------

    [Fact]
    public async Task The_openapi_document_covers_every_registered_api_endpoint_with_a_documented_response()
    {
        using var client = _factory.OwnerClient();
        var raw = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        using var document = JsonDocument.Parse(raw);
        var paths = document.RootElement.GetProperty("paths");

        var apiRoutes = HostEndpoints()
            .OfType<RouteEndpoint>()
            .Select(e => "/" + e.RoutePattern.RawText?.TrimStart('/'))
            .Where(r => r.StartsWith("/api/", StringComparison.Ordinal))
            .Select(NormaliseForOpenApi)
            .Distinct()
            .ToList();

        apiRoutes.ShouldNotBeEmpty();

        foreach (var route in apiRoutes)
        {
            paths.TryGetProperty(route, out var pathItem).ShouldBeTrue($"OpenAPI is missing a path for {route}");

            // Every operation on the path carries a human summary (the documented contract) and at least
            // one response — the described shape a client can expect, never an undocumented endpoint.
            foreach (var operation in pathItem.EnumerateObject())
            {
                operation.Value.TryGetProperty("summary", out var summary).ShouldBeTrue(
                    $"{route} {operation.Name} has no summary");
                summary.GetString().ShouldNotBeNullOrWhiteSpace();

                operation.Value.TryGetProperty("responses", out var responses).ShouldBeTrue(
                    $"{route} {operation.Name} documents no response");
                responses.EnumerateObject().Any().ShouldBeTrue($"{route} {operation.Name} documents no response");
            }
        }
    }

    // --- Helpers ------------------------------------------------------------------------------------

    private List<Endpoint> HostEndpoints()
    {
        // Force the host to build so the endpoint registry is materialised, then read every data source.
        using var _ = _factory.OwnerClient();
        return _factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .ToList();
    }

    private static RouteEndpoint RouteEndpointFor(string pattern, bool authorize)
    {
        var builder = new RouteEndpointBuilder(
            _ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), order: 0);
        if (authorize)
        {
            builder.Metadata.Add(new AuthorizeAttribute(ApiSecurityExtensions.ReadPolicy));
        }

        return (RouteEndpoint)builder.Build();
    }

    private static string NormaliseForOpenApi(string route) =>
        RouteConstraintPattern().Replace(route, "{$1}");

    [GeneratedRegex(@"\{([^:}]+)(:[^}]+)?\}")]
    private static partial Regex RouteConstraintPattern();
}
