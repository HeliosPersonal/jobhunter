using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using Microsoft.EntityFrameworkCore;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace JobHunter.ArchitectureTests;

/// <summary>
/// The convention rules from coding-standards §2 that are about shape and idiom rather than layering:
/// Dapper never writes (rule 4), only <c>SystemClock</c> reads the ambient clock (rule 5), nothing
/// outside <c>src/Aspire</c> references the AppHost (rule 6), every entity configuration is
/// <c>internal sealed</c> (rule 7), and Infrastructure exposes only extension/options types (rule 8).
/// </summary>
public sealed class ConventionRulesTests
{
    [Fact]
    public void Rule4_DapperQueries_neverWrite()
    {
        // NetArchTest 1.3.2 has no call-graph assertion, so this rule is enforced at the source level:
        // no type in the Queries namespace may name a Dapper write method.
        var writes = SourceScan
            .ForPattern(@"\.(ExecuteAsync|Execute|ExecuteScalar|ExecuteScalarAsync)\b")
            .ExcludingFiles() // scan the whole tree; only Queries files would legitimately trip it
            .Matches
            .Where(m => m.Contains("Query", StringComparison.OrdinalIgnoreCase))
            .ToList();

        writes.ShouldBeEmpty("Dapper read queries must never call a write method: " + string.Join("; ", writes));
    }

    [Fact]
    public void Rule5_onlySystemClock_readsTheAmbientClock()
    {
        var matches = SourceScan
            .ForPattern(@"DateTime(Offset)?\.(Now|UtcNow)")
            .ExcludingType<JobHunter.Domain.Abstractions.SystemClock>()
            .Matches;

        matches.ShouldBeEmpty(
            "Only SystemClock may read the ambient clock (architecture rule 5): " + string.Join("; ", matches));
    }

    [Fact]
    public void Rule6_nothingOutsideAspire_referencesTheAppHost()
    {
        // The AppHost assembly is not referenced by any production project, so it is not even loadable
        // from here. Assert its types never appear as a dependency of the layered assemblies.
        var result = Types.InAssemblies(
            [
                typeof(JobHunter.Domain.Abstractions.IClock).Assembly,
                typeof(JobHunter.Application.Common.Telemetry).Assembly,
                typeof(JobHunter.Infrastructure.DependencyInjection).Assembly,
            ])
            .Should()
            .NotHaveDependencyOn("JobHunter.AppHost")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(LayeringRulesTests.FailureMessage(result));
    }

    [Fact]
    public void Rule7_everyEntityConfiguration_isInternalSealed()
    {
        var result = Types.InAssembly(typeof(JobHunterDbContext).Assembly)
            .That()
            .ImplementInterface(typeof(IEntityTypeConfiguration<>))
            .Should()
            .BeSealed()
            .And()
            .NotBePublic()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(LayeringRulesTests.FailureMessage(result));
    }

    [Fact]
    public void Rule8_public_infrastructureTypes_areExtensionOrOptionsOrPorts()
    {
        // A public Infrastructure type must be a DI extension class, an *Options class, a *Factory, a
        // *Configuration helper, a repository/query seam, or an *Attribute — never a leaked service.
        var offenders = Types.InAssembly(typeof(JobHunter.Infrastructure.DependencyInjection).Assembly)
            .That()
            .ArePublic()
            .And()
            .AreNotAbstract()
            .Should()
            .MeetCustomRule(new PublicInfrastructureTypeRule())
            .GetResult();

        offenders.IsSuccessful.ShouldBeTrue(LayeringRulesTests.FailureMessage(offenders));
    }
}
