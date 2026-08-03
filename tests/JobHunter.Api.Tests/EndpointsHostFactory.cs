using JobHunter.Domain.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace JobHunter.Api.Tests;

/// <summary>
/// Boots the real <see cref="Program"/> with the F9 read ports replaced by NSubstitute fakes, so the
/// search and job endpoints can be exercised end-to-end (routing, auth, the scope-plus-Owner gate, the
/// RFC 7807 problem shapes) with zero network and a controllable read side. Authentication is the same
/// header-driven test scheme <see cref="ApiHostFactory"/> uses.
/// </summary>
public sealed class EndpointsHostFactory : WebApplicationFactory<Program>
{
    /// <summary>The Owner subject the host is configured with; a token for any other subject is a 403.</summary>
    public const string OwnerSubject = "owner-subject-123";

    public ISearchQuery Search { get; } = Substitute.For<ISearchQuery>();

    public IJobRepository Jobs { get; } = Substitute.For<IJobRepository>();

    public ICompanyRepository Companies { get; } = Substitute.For<ICompanyRepository>();

    public ILiveJobsQuery LiveJobs { get; } = Substitute.For<ILiveJobsQuery>();

    /// <summary>A client presenting a valid Owner token with the given scope (read by default).</summary>
    public HttpClient OwnerClient(string scope = "jobhunter:read")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, scope);
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, OwnerSubject);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");

        builder.UseSetting("ConnectionStrings:JobHunter", "Host=127.0.0.1;Port=1;Database=jobhunter;Username=test;Password=test");
        builder.UseSetting("ConnectionStrings:Messaging", "amqp://guest:guest@127.0.0.1:5672");
        builder.UseSetting("Typesense:BaseUrl", "http://127.0.0.1:1");
        builder.UseSetting("Typesense:ApiKey", "test-key");
        builder.UseSetting("Typesense:EnvironmentPrefix", "test");
        builder.UseSetting("Keycloak:OwnerSubject", OwnerSubject);

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            // Replace the read ports with controllable fakes so no dependency is dialled at request time.
            services.RemoveAll<ISearchQuery>();
            services.AddSingleton(Search);
            services.RemoveAll<IJobRepository>();
            services.AddScoped(_ => Jobs);
            services.RemoveAll<ICompanyRepository>();
            services.AddScoped(_ => Companies);
            services.RemoveAll<ILiveJobsQuery>();
            services.AddScoped(_ => LiveJobs);
        });
    }
}
