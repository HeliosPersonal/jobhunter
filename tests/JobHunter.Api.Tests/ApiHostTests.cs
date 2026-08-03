using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

namespace JobHunter.Api.Tests;

/// <summary>
/// The host behaviours T04 is accountable for, asserted against the real <see cref="Program"/> pipeline
/// (WebApplicationFactory, zero network): the fallback-deny policy refuses any request that carries no
/// authentication — even to a path with no endpoint (AC-06); the liveness and readiness probes are the
/// only anonymous surface; the scope-plus-Owner gate on the admin-scoped <c>/health</c> endpoint refuses
/// a valid token issued for a subject other than the Owner (a 403, never a 200); the OpenAPI document
/// and Scalar UI are served; and an error carries an RFC 7807 body with no internal detail.
/// </summary>
public sealed class ApiHostTests : IClassFixture<ApiHostFactory>
{
    private readonly ApiHostFactory _factory;

    public ApiHostTests(ApiHostFactory factory) => _factory = factory;

    private HttpClient OwnerClient(string scope = "jobhunter:read jobhunter:admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, scope);
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, ApiHostFactory.OwnerSubject);
        return client;
    }

    // --- Fallback-deny (AC-06) ---------------------------------------------------------------------

    [Fact]
    public async Task An_unauthenticated_request_to_a_path_with_no_declared_policy_is_refused()
    {
        using var client = _factory.CreateClient();

        // No endpoint exists at this path and no policy is declared for it; the host's fallback policy
        // (RequireAuthenticatedUser) is evaluated regardless and refuses the tokenless caller — a new
        // endpoint is protected by default (security §2).
        var response = await client.GetAsync(new Uri("/some/unmapped/path", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_authenticated_request_passes_the_fallback_policy_then_falls_through_to_404()
    {
        using var client = OwnerClient();

        // The same unmapped path, now authenticated: the fallback policy passes, so the refusal is no
        // longer 401 — routing then finds no endpoint and returns 404. This is the pair that proves the
        // fallback policy is RequireAuthenticatedUser and nothing stronger.
        var response = await client.GetAsync(new Uri("/some/unmapped/path", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // --- Anonymous surface -------------------------------------------------------------------------

    [Fact]
    public async Task The_liveness_probe_is_anonymous_and_exposes_no_business_data()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/alive", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldBe("Healthy");
    }

    [Fact]
    public async Task The_readiness_probe_is_anonymous()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/ready", UriKind.Relative));

        // Reachable without a token; its status reflects dependency health, not authorization.
        response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_full_health_endpoint_is_not_anonymous()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // --- Scope + Owner (ADR-0014) ------------------------------------------------------------------

    [Fact]
    public async Task The_admin_endpoint_admits_the_owner_with_the_admin_scope()
    {
        using var client = OwnerClient("jobhunter:admin");

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        // Authorised — the response reflects dependency health (503 here, dependencies down), never a
        // 401 or 403. Authorization passed.
        response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_valid_admin_token_for_a_subject_other_than_the_owner_is_refused_with_403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, "jobhunter:admin");
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, "someone-else");

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        // Scope is present but the subject is not the Owner — 403, never admitted (invariant 9).
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        client.Dispose();
    }

    [Fact]
    public async Task The_owner_without_the_admin_scope_is_refused_the_admin_endpoint()
    {
        using var client = OwnerClient("jobhunter:read");

        // The Owner subject, but only the read scope — the admin endpoint still refuses. Scope is
        // checked in addition to the subject, never bypassed by it.
        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- OpenAPI + Scalar --------------------------------------------------------------------------

    [Fact]
    public async Task The_openapi_document_is_served_to_the_owner()
    {
        using var client = OwnerClient();

        var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("openapi");
    }

    [Fact]
    public async Task The_scalar_reference_ui_is_served_to_the_owner()
    {
        using var client = OwnerClient();

        var response = await client.GetAsync(new Uri("/scalar/v1", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // --- RFC 7807 ----------------------------------------------------------------------------------

    [Fact]
    public async Task An_error_response_carries_an_rfc7807_body_with_no_internal_detail()
    {
        using var client = OwnerClient();

        // An authenticated request to an unmapped path is a 404; UseStatusCodePages + AddProblemDetails
        // render it as application/problem+json. The body carries the standard fields and a correlating
        // traceId — never a stack trace or an exception type (security §5).
        var response = await client.GetAsync(new Uri("/some/unmapped/path", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(404);

        var raw = await response.Content.ReadAsStringAsync();
        raw.ShouldContain("traceId");
        raw.ShouldNotContain("StackTrace", Case.Insensitive);
        raw.ShouldNotContain("Exception", Case.Insensitive);
    }

    private sealed record ProblemShape(int? Status, string? Title, string? Type);
}
