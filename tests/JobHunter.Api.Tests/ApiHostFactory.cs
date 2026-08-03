using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Api.Tests;

/// <summary>
/// Boots the real <see cref="Program"/> over in-memory configuration and zero network. The host runs in
/// the Development environment so Infisical's secret fetch is skipped; a fixed Owner subject is
/// configured so the scope-plus-Owner policies are genuinely enforced. Authentication is swapped for a
/// header-driven test scheme (still the host's real fallback-deny policy, authorization middleware and
/// scoped policies) so a test can present an Owner token, a wrong-subject token, or none at all with no
/// Keycloak in the loop. Connection strings are present-but-unreachable: they satisfy the startup
/// validators without any dependency being contacted at boot.
/// </summary>
public sealed class ApiHostFactory : WebApplicationFactory<Program>
{
    /// <summary>The Owner subject the host is configured with; a token for any other subject is a 403.</summary>
    public const string OwnerSubject = "owner-subject-123";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");

        // Required connection strings — parseable so the options validators pass; never connected to at
        // boot (EF only configures the provider; the health checks connect lazily on /ready).
        builder.UseSetting("ConnectionStrings:JobHunter", "Host=127.0.0.1;Port=1;Database=jobhunter;Username=test;Password=test");
        builder.UseSetting("ConnectionStrings:Messaging", "amqp://guest:guest@127.0.0.1:5672");
        // No Cache connection string: the in-memory rate limiter is used, so nothing dials Redis at boot.

        // Typesense options — present so AddJobHunterSearch's startup validators pass; the base URL is
        // present-but-unreachable and only dialled lazily on a search, never at boot.
        builder.UseSetting("Typesense:BaseUrl", "http://127.0.0.1:1");
        builder.UseSetting("Typesense:ApiKey", "test-key");
        builder.UseSetting("Typesense:EnvironmentPrefix", "test");

        // Enforce the Owner-subject check even in Development (it is otherwise disabled without a
        // configured Owner), so the wrong-subject 403 behaviour can be asserted.
        builder.UseSetting("Keycloak:OwnerSubject", OwnerSubject);

        // ConfigureTestServices runs after the host's own registrations, so the test authentication
        // scheme becomes the resolved default and the probe endpoints join the real routing table.
        builder.ConfigureTestServices(services =>
            // Replace Keycloak JWT validation with the header-driven test scheme, and make it the default
            // so RequireAuthenticatedUser and the scope policies evaluate against the synthesised principal.
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { }));
    }
}
