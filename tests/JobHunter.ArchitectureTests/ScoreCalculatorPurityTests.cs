using System.Reflection;
using JobHunter.Application.Ranking;
using Shouldly;
using Xunit;

namespace JobHunter.ArchitectureTests;

/// <summary>
/// F4 T07 / ADR-F4-0001 / QG-3: <c>ScoreCalculator</c> is the ordering decision, and the ordering must be
/// provably deterministic. That is enforced structurally, not just by the property tests: the type is a
/// <strong>static class</strong> with <strong>no instance state</strong>, and its public entry point takes
/// <strong>no clock, no repository and no options object</strong> — every input is an explicit value. A
/// signature that reached for the ambient time or a database could never satisfy the determinism the digest
/// depends on, so it is made unrepresentable here.
/// </summary>
public sealed class ScoreCalculatorPurityTests
{
    private static readonly string[] BannedParameterFragments =
    [
        "Clock", "Repository", "Options", "DbContext", "HttpClient", "Logger", "Provider", "Service",
    ];

    [Fact]
    public void ScoreCalculator_is_a_static_class_with_no_instance_state()
    {
        var type = typeof(ScoreCalculator);

        type.IsAbstract.ShouldBeTrue("ScoreCalculator must be static (abstract+sealed).");
        type.IsSealed.ShouldBeTrue("ScoreCalculator must be static (abstract+sealed).");

        var instanceFields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        instanceFields.ShouldBeEmpty("A pure function type holds no instance state.");
    }

    [Fact]
    public void Calculate_takes_no_clock_no_repository_and_no_options_object()
    {
        var method = typeof(ScoreCalculator).GetMethod(nameof(ScoreCalculator.Calculate))!;

        var offenders = method
            .GetParameters()
            .Select(p => p.ParameterType.Name)
            .Where(name => BannedParameterFragments.Any(fragment =>
                name.Contains(fragment, StringComparison.Ordinal)))
            .ToList();

        offenders.ShouldBeEmpty(
            "ScoreCalculator.Calculate must take only explicit values, no ambient dependency (QG-3): "
            + string.Join("; ", offenders));
    }

    [Fact]
    public void Every_public_method_on_ScoreCalculator_is_static()
    {
        var instanceMethods = typeof(ScoreCalculator)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToList();

        instanceMethods.ShouldBeEmpty("A pure function type exposes only static methods.");
    }
}
