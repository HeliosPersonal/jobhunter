using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunter.Api.Tests;

/// <summary>
/// A test authentication scheme that stands in for Keycloak JWT validation with zero network: it
/// synthesises the principal from two request headers so a test can present an Owner token, a
/// wrong-subject token or no token at all. Absent both headers it returns
/// <see cref="AuthenticateResult.NoResult"/>, so the request is treated as unauthenticated and meets the
/// host's fallback-deny policy exactly as a tokenless call would in production.
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public const string ScopeHeader = "X-Test-Scope";
    public const string SubjectHeader = "X-Test-Sub";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var hasScope = Request.Headers.TryGetValue(ScopeHeader, out var scope);
        var hasSubject = Request.Headers.TryGetValue(SubjectHeader, out var subject);

        if (!hasScope && !hasSubject)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>();
        if (hasScope)
        {
            claims.Add(new Claim("scope", scope.ToString()));
        }

        if (hasSubject)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, subject.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
