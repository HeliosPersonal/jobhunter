using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Smoke;

/// <summary>
/// AC-09: a missing required option fails the host at startup, naming the offending key, rather than
/// deferring the failure to first use. Uses a factory whose configuration omits the Postgres connection
/// string, then asserts the host refuses to build.
/// </summary>
public sealed class StartupValidationTests
{
    [Fact]
    public void Startup_WithMissingConnectionString_FailsFastNamingTheKey()
    {
        using var factory = new WebApplicationFactory<JobHunter.Api.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");

                // Supply everything except ConnectionStrings:JobHunter, which the composition root
                // requires. Its absence must fail the host at build time, naming the key (AC-09).
                builder.UseSetting("Messaging:ConnectionString", "amqp://guest:guest@localhost:5672");
            });

        // AddJobHunterInfrastructure throws when ConnectionStrings:JobHunter is absent (AC-09).
        var exception = Should.Throw<InvalidOperationException>(() => factory.CreateClient());
        exception.Message.ShouldContain("JobHunter");
    }
}
