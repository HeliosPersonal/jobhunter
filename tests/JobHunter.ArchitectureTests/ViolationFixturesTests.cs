using Microsoft.EntityFrameworkCore;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace JobHunter.ArchitectureTests;

/// <summary>
/// The other half of T12: every rule ships a deliberately-violating fixture in
/// <c>JobHunter.ArchitectureTests.Violations</c>, and this suite proves each guard actually goes red
/// against it. "An assertion that has never gone red is an assertion nobody has verified"
/// (test-plan §Architecture). The production rules in <see cref="LayeringRulesTests"/> and
/// <see cref="ConventionRulesTests"/> scope the Violations namespace / <c>tests</c> tree away, so these
/// fixtures never break the real build.
/// </summary>
public sealed class ViolationFixturesTests
{
    private const string ViolationsNamespace = "JobHunter.ArchitectureTests.Violations";

    [Fact]
    public void Rule1_fixture_provesDomainDependencyGuard_canFail()
    {
        var result = FixtureNamed("DomainDependsOnInfrastructure")
            .Should()
            .NotHaveDependencyOn("JobHunter.Infrastructure")
            .GetResult();

        result.IsSuccessful.ShouldBeFalse();
    }

    [Fact]
    public void Rule2_fixture_provesApplicationDependencyGuard_canFail()
    {
        var result = FixtureNamed("ApplicationDependsOnInfrastructure")
            .Should()
            .NotHaveDependencyOn("JobHunter.Infrastructure")
            .GetResult();

        result.IsSuccessful.ShouldBeFalse();
    }

    [Fact]
    public void Rule2b_fixture_provesInfrastructureNotHostsGuard_canFail()
    {
        var result = FixtureNamed("InfrastructureDependsOnWorkerHost")
            .Should()
            .NotHaveDependencyOn("JobHunter.Worker")
            .GetResult();

        result.IsSuccessful.ShouldBeFalse();
    }

    [Fact]
    public void Rule3_fixture_provesContractsReferencesNothingGuard_canFail()
    {
        var result = FixtureNamed("ContractsDependsOnDomain")
            .Should()
            .NotHaveDependencyOn("JobHunter.Domain")
            .GetResult();

        result.IsSuccessful.ShouldBeFalse();
    }

    [Fact]
    public void Rule4_fixture_provesDapperWriteGuard_canFail()
    {
        var writes = SourceScan
            .ForPattern(@"\.(ExecuteAsync|Execute|ExecuteScalar|ExecuteScalarAsync)\b")
            .InDirectory(ViolationsDirectory())
            .Matches
            .Where(m => m.Contains("Query", StringComparison.OrdinalIgnoreCase))
            .ToList();

        writes.ShouldNotBeEmpty();
    }

    [Fact]
    public void Rule5_fixture_provesAmbientClockGuard_canFail()
    {
        var matches = SourceScan
            .ForPattern(@"DateTime(Offset)?\.(Now|UtcNow)")
            .ExcludingType<JobHunter.Domain.Abstractions.SystemClock>()
            .InDirectory(ViolationsDirectory())
            .Matches;

        matches.ShouldNotBeEmpty();
    }

    [Fact]
    public void Rule6_fixture_provesAppHostIsolationGuard_canFail()
    {
        // The AppHost cannot be referenced from a test project by construction (that is rule 6 itself),
        // so the guard mechanism is exercised against another host with the identical assertion.
        var result = FixtureNamed("DependsOnApiHost")
            .Should()
            .NotHaveDependencyOn("JobHunter.Api")
            .GetResult();

        result.IsSuccessful.ShouldBeFalse();
    }

    [Fact]
    public void Rule7_fixture_provesEntityConfigurationGuard_canFail()
    {
        var result = Types.InAssembly(typeof(ViolationFixturesTests).Assembly)
            .That()
            .ResideInNamespace(ViolationsNamespace)
            .And()
            .ImplementInterface(typeof(IEntityTypeConfiguration<>))
            .Should()
            .NotBePublic()
            .GetResult();

        result.IsSuccessful.ShouldBeFalse();
    }

    [Fact]
    public void Rule8_fixture_provesInfrastructureSurfaceGuard_canFail()
    {
        var result = FixtureNamed("LeakedConcreteService")
            .Should()
            .MeetCustomRule(new PublicInfrastructureTypeRule())
            .GetResult();

        result.IsSuccessful.ShouldBeFalse();
    }

    private static PredicateList FixtureNamed(string typeName) =>
        Types.InAssembly(typeof(ViolationFixturesTests).Assembly)
            .That()
            .ResideInNamespace(ViolationsNamespace)
            .And()
            .HaveName(typeName);

    private static string ViolationsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "JobHunter.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate the repository root (JobHunter.slnx).");
        }

        return Path.Combine(dir.FullName, "tests", "JobHunter.ArchitectureTests", "Violations");
    }
}
