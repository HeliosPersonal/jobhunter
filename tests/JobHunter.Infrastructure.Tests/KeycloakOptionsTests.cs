using JobHunter.Api;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests;

public sealed class KeycloakOptionsTests
{
    [Fact]
    public void Is_configured_when_an_authority_is_present()
    {
        new KeycloakOptions { Authority = "https://keycloak/realms/jobhunter" }.IsConfigured.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Is_not_configured_without_an_authority(string authority)
    {
        new KeycloakOptions { Authority = authority }.IsConfigured.ShouldBeFalse();
    }

    [Fact]
    public void Defaults_are_the_documented_audience_and_https_requirement()
    {
        var options = new KeycloakOptions();

        options.Audience.ShouldBe("jobhunter-api");
        options.RequireHttpsMetadata.ShouldBeTrue();
    }
}
