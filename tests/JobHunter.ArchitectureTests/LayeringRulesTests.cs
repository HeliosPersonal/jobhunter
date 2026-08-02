using JobHunter.Application.Common;
using JobHunter.Contracts;
using JobHunter.Domain.Abstractions;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace JobHunter.ArchitectureTests;

/// <summary>
/// One fact per dependency-direction rule (coding-standards §2, SAD §10 QG-2). The corresponding
/// deliberately-violating fixtures live in <c>JobHunter.ArchitectureTests.Violations</c> and are proven
/// to fail by <see cref="ViolationFixturesTests"/>.
/// </summary>
public sealed class LayeringRulesTests
{
    private const string Infrastructure = "JobHunter.Infrastructure";
    private const string Application = "JobHunter.Application";

    [Fact]
    public void Rule1_Domain_dependsOnly_onMicrosoftExtensionsAbstractions()
    {
        var result = Types.InAssembly(typeof(IClock).Assembly)
            .Should()
            .NotHaveDependencyOnAny(
                Infrastructure,
                Application,
                "JobHunter.Contracts",
                "Microsoft.EntityFrameworkCore",
                "Wolverine",
                "Npgsql",
                "Dapper",
                "Hangfire")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureMessage(result));
    }

    [Fact]
    public void Rule2_Application_doesNotDependOn_Infrastructure_orHosts()
    {
        var result = Types.InAssembly(typeof(Telemetry).Assembly)
            .Should()
            .NotHaveDependencyOnAny(
                Infrastructure,
                "JobHunter.Scrapers",
                "JobHunter.Claude",
                "JobHunter.Search",
                "JobHunter.Api",
                "JobHunter.Worker",
                "JobHunter.Telegram")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureMessage(result));
    }

    [Fact]
    public void Rule2b_Infrastructure_doesNotDependOn_theHosts()
    {
        var result = Types.InAssembly(typeof(JobHunter.Infrastructure.DependencyInjection).Assembly)
            .Should()
            .NotHaveDependencyOnAny("JobHunter.Api", "JobHunter.Worker", "JobHunter.Telegram")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureMessage(result));
    }

    [Fact]
    public void Rule3_Contracts_referencesNothing_inTheSolution()
    {
        var result = Types.InAssembly(typeof(IIntegrationEvent).Assembly)
            .Should()
            .NotHaveDependencyOnAny(
                "JobHunter.Domain",
                Application,
                Infrastructure,
                "JobHunter.Api",
                "JobHunter.Worker",
                "JobHunter.Telegram")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureMessage(result));
    }

    internal static string FailureMessage(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : "Offending types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
