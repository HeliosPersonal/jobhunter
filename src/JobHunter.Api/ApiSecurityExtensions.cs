using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace JobHunter.Api;

/// <summary>
/// API authentication and authorization wiring (ADR-0014). Keycloak JWT bearer, two scopes
/// (<c>jobhunter:read</c>, <c>jobhunter:admin</c>), default-deny, and an Owner-subject check so a valid
/// token for a different subject is a 403. Excluded from coverage — it is host composition, exercised
/// by the API's integration tests, not unit-tested.
/// </summary>
[ExcludeFromCodeCoverage]
public static class ApiSecurityExtensions
{
    /// <summary>The scope required for read models and search.</summary>
    public const string ReadPolicy = "jobhunter:read";

    /// <summary>The scope required for mutations and operational endpoints (incl. /health).</summary>
    public const string AdminPolicy = "jobhunter:admin";

    public static WebApplicationBuilder AddApiSecurity(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<KeycloakOptions>()
            .Bind(builder.Configuration.GetSection(KeycloakOptions.SectionName))
            .Validate(
                o => builder.Environment.IsDevelopment() || o.IsConfigured,
                "Keycloak:Authority is required outside Development (ADR-0014).")
            .ValidateOnStart();

        var keycloak = builder.Configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>()
                       ?? new KeycloakOptions();

        var authenticationBuilder = builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme);

        if (keycloak.IsConfigured)
        {
            authenticationBuilder.AddJwtBearer(options =>
            {
                options.Authority = keycloak.Authority;
                options.Audience = keycloak.Audience;
                options.RequireHttpsMetadata = keycloak.RequireHttpsMetadata;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateAudience = true,
                    ValidateIssuer = true,
                    ValidateLifetime = true,
                    ValidIssuer = keycloak.Authority,
                };
            });
        }
        else
        {
            // Development without a local Keycloak: register the scheme so [Authorize] resolves, but no
            // real issuer. Never reached in Staging/Production — the options validator fails first.
            authenticationBuilder.AddJwtBearer();
        }

        builder.Services.AddAuthorizationBuilder()
            // Fallback-deny (AC-06, security §2): an endpoint registered without an explicit policy is
            // still refused for an unauthenticated caller — a new endpoint is protected by default and
            // must opt out deliberately. The endpoint-convention suite (T10) asserts every endpoint
            // additionally declares its own scope.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(ReadPolicy, policy => BuildScopePolicy(policy, keycloak, ReadPolicy))
            .AddPolicy(AdminPolicy, policy => BuildScopePolicy(policy, keycloak, AdminPolicy));

        return builder;
    }

    private static void BuildScopePolicy(AuthorizationPolicyBuilder policy, KeycloakOptions keycloak, string scope)
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
            ScopeAuthorization.Satisfies(context.User, scope, keycloak.OwnerSubject));
    }
}
