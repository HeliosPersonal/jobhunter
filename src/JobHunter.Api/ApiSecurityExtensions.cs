using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
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
            .AddPolicy(ReadPolicy, policy => BuildScopePolicy(policy, keycloak, ReadPolicy))
            .AddPolicy(AdminPolicy, policy => BuildScopePolicy(policy, keycloak, AdminPolicy));

        return builder;
    }

    private static void BuildScopePolicy(AuthorizationPolicyBuilder policy, KeycloakOptions keycloak, string scope)
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context => HasScope(context.User, scope) && IsOwner(context.User, keycloak));
    }

    private static bool HasScope(ClaimsPrincipal user, string requiredScope)
    {
        // Keycloak emits scopes in a space-delimited `scope` claim.
        var scopeClaim = user.FindFirst("scope")?.Value;
        return scopeClaim is not null
               && scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(requiredScope);
    }

    private static bool IsOwner(ClaimsPrincipal user, KeycloakOptions keycloak)
    {
        // No configured Owner subject (local dev) means the subject check is not enforced.
        if (string.IsNullOrWhiteSpace(keycloak.OwnerSubject))
        {
            return true;
        }

        var subject = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
        return string.Equals(subject, keycloak.OwnerSubject, StringComparison.Ordinal);
    }
}
