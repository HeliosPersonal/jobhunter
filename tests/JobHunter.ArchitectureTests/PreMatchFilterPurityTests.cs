using System.Reflection;
using JobHunter.Application.Matching;
using Shouldly;
using Xunit;

namespace JobHunter.ArchitectureTests;

/// <summary>
/// F4 T12 / ADR-F4-0003: <c>PreMatchFilter</c> is a <em>factual</em> gate that must read only the job's
/// enrichment, the job's posting facts and the Profile — never the match store, the score store or the CV. That
/// is enforced structurally, the same way <see cref="ScoreCalculatorPurityTests"/> pins <c>ScoreCalculator</c>:
///
/// <list type="bullet">
/// <item><description>the type is a <strong>static class with no instance state</strong>, and
/// <c>Evaluate</c> takes <strong>no clock, repository, options object or query</strong> — every input is an
/// explicit value, so it cannot reach a database to decide;</description></item>
/// <item><description>its source names <strong>no <c>matches</c>/<c>scores</c> table, no query port and no CV
/// type</strong> — the lifecycle fact arrives as a <c>bool</c> the caller resolves, so the filter itself stays
/// blind to the tables the architecture forbids it to touch.</description></item>
/// </list>
/// </summary>
public sealed class PreMatchFilterPurityTests
{
    private static readonly string[] BannedParameterFragments =
    [
        "Clock", "Repository", "Options", "DbContext", "HttpClient", "Logger", "Provider", "Service", "Query",
    ];

    // Tokens that would mean the filter is reaching for the match/score store or the CV — the very things the
    // caller is responsible for resolving and handing in as plain values.
    private static readonly string[] BannedSourceTokens =
    [
        "ICurrentMatchQuery", "IScoreRepository", "IMatchRepository", "CvVersion", "MatchPrompt",
        "matches", "scores", "DbContext", "connection",
    ];

    [Fact]
    public void PreMatchFilter_is_a_static_class_with_no_instance_state()
    {
        var type = typeof(PreMatchFilter);

        type.IsAbstract.ShouldBeTrue("PreMatchFilter must be static (abstract+sealed).");
        type.IsSealed.ShouldBeTrue("PreMatchFilter must be static (abstract+sealed).");

        var instanceFields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        instanceFields.ShouldBeEmpty("A pure function type holds no instance state.");
    }

    [Fact]
    public void Evaluate_takes_no_clock_no_repository_no_options_and_no_query()
    {
        var method = typeof(PreMatchFilter).GetMethod(nameof(PreMatchFilter.Evaluate))!;

        var offenders = method
            .GetParameters()
            .Select(p => p.ParameterType.Name)
            .Where(name => BannedParameterFragments.Any(fragment =>
                name.Contains(fragment, StringComparison.Ordinal)))
            .ToList();

        offenders.ShouldBeEmpty(
            "PreMatchFilter.Evaluate must take only explicit values, no ambient dependency: "
            + string.Join("; ", offenders));
    }

    [Fact]
    public void Every_public_method_on_PreMatchFilter_is_static()
    {
        var instanceMethods = typeof(PreMatchFilter)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToList();

        instanceMethods.ShouldBeEmpty("A pure function type exposes only static methods.");
    }

    [Fact]
    public void PreMatchFilter_source_names_no_match_or_score_store_and_no_cv()
    {
        var path = LocateFilterSource();
        var lines = File.ReadAllLines(path);
        var offenders = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            // The rule is about code, not prose: a doc comment explaining why the filter never touches the
            // match store is not a use of it.
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*'))
            {
                continue;
            }

            foreach (var token in BannedSourceTokens)
            {
                if (lines[i].Contains(token, StringComparison.Ordinal))
                {
                    offenders.Add($"{i + 1}: {token} → {lines[i].Trim()}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "PreMatchFilter must read only enrichment, job facts and the Profile — never the match/score store "
            + "or the CV (ADR-F4-0003): " + string.Join("; ", offenders));
    }

    private static string LocateFilterSource()
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

        return Directory
            .EnumerateFiles(Path.Combine(dir.FullName, "src"), "PreMatchFilter.cs", SearchOption.AllDirectories)
            .Single(p => !p.Replace('\\', '/').Contains("/obj/", StringComparison.Ordinal)
                         && !p.Replace('\\', '/').Contains("/bin/", StringComparison.Ordinal));
    }
}
