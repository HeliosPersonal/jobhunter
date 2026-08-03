using System.Globalization;
using JobHunter.Claude.Enrichment;
using JobHunter.Domain.Abstractions;
using Shouldly;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace JobHunter.Claude.Tests.Enrichment;

/// <summary>
/// F3's second credibility suite: the <strong>golden set</strong> (testing-strategy §F3, T13 done-when).
/// Fifty recorded tool-use payloads are driven through the <em>real</em> <see cref="EnrichmentResultParser"/>
/// — the same eight-step tolerant parser production uses — and each parsed enrichment is asserted to fall
/// within a labelled <strong>band</strong> rather than at an exact value. A band ("aiUsage ∈ {High, Medium}",
/// "salary between 90k and 200k USD") is what keeps the suite honest against a non-deterministic model
/// without being flaky: a recorded payload is deterministic, but the assertion tolerates the drift a live
/// model would show, so the nightly live-drift job can reuse these same bands against fresh model output.
///
/// <para>The corpus also carries the repair and rejection cases the parser must handle at 03:00 — an
/// inverted salary swapped, a confidence clamped, an unknown currency dropped, an unrecognised enum
/// degraded to <c>Unknown</c>, and the hard failures (empty reasons, malformed JSON) that must be recorded,
/// never thrown (QG-3, invariant 4). Every successful case is asserted to carry at least one reason.</para>
/// </summary>
public sealed class GoldenEnrichmentSetTests
{
    private static readonly Guid EnrichmentId = Guid.Parse("00000000-0000-0000-0000-0000000000E1");
    private static readonly Guid JobId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000B1");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 3, 3, 0, 0, TimeSpan.Zero);

    private static readonly List<GoldenCase> Corpus = LoadCorpus();

    private readonly EnrichmentResultParser _parser = new();

    [Fact]
    public void The_golden_set_has_at_least_fifty_labelled_cases()
    {
        Corpus.Count.ShouldBeGreaterThanOrEqualTo(50);
    }

    [Fact]
    public void Every_case_lands_within_its_labelled_band()
    {
        var failures = new List<string>();

        foreach (var c in Corpus)
        {
            var outcome = _parser.Parse(
                new EnrichmentParseRequest(EnrichmentId, JobId, RunId, "enrich-v1", CreatedAt, c.Raw));

            foreach (var problem in Check(c, outcome))
            {
                failures.Add($"#{c.Id} ({c.Why}): {problem}");
            }
        }

        failures.ShouldBeEmpty(
            "Golden cases fell outside their labelled bands:" + Environment.NewLine
            + string.Join(Environment.NewLine, failures));
    }

    private static IEnumerable<string> Check(GoldenCase c, EnrichmentParseOutcome outcome)
    {
        if (!c.Parses)
        {
            if (outcome.IsSuccess)
            {
                yield return "expected a recorded parse failure but it parsed";
            }
            else if (c.FailReason is not null
                && !(outcome.FailureReason?.Contains(c.FailReason, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                yield return $"failure reason '{outcome.FailureReason}' did not mention '{c.FailReason}'";
            }

            yield break;
        }

        if (!outcome.IsSuccess)
        {
            yield return $"expected a successful parse but it failed: {outcome.FailureReason}";
            yield break;
        }

        var e = outcome.Enrichment!;

        // Invariant 4 holds for every successful case, unconditionally.
        if (e.Reasons.Count < Math.Max(1, c.MinReasons))
        {
            yield return $"expected at least {Math.Max(1, c.MinReasons)} reason(s), got {e.Reasons.Count}";
        }

        if (c.Remote is { } remote && e.IsRemote != remote)
        {
            yield return $"isRemote expected {remote}, got {e.IsRemote}";
        }

        if (c.Contractor is { } contractor && e.IsContractorFriendly != contractor)
        {
            yield return $"isContractorFriendly expected {contractor}, got {e.IsContractorFriendly}";
        }

        if (c.AiUsageIn.Count > 0 && !c.AiUsageIn.Contains(e.AiUsage.ToString()))
        {
            yield return $"aiUsage {e.AiUsage} not in [{string.Join(", ", c.AiUsageIn)}]";
        }

        if (c.TimezoneIn.Count > 0 && !c.TimezoneIn.Contains(e.TimezoneBand.ToString()))
        {
            yield return $"timezoneBand {e.TimezoneBand} not in [{string.Join(", ", c.TimezoneIn)}]";
        }

        if (c.StageIn.Count > 0 && !c.StageIn.Contains(e.CompanyStage.ToString()))
        {
            yield return $"companyStage {e.CompanyStage} not in [{string.Join(", ", c.StageIn)}]";
        }

        if (c.Salary is SalaryExpectation.Present)
        {
            if (e.Salary is null)
            {
                yield return "expected a salary but it was dropped";
            }
            else
            {
                if (c.SalaryFloor is { } floor && e.Salary.Min < floor)
                {
                    yield return $"salary min {e.Salary.Min} below band floor {floor}";
                }

                if (c.SalaryCeil is { } ceil && e.Salary.Max > ceil)
                {
                    yield return $"salary max {e.Salary.Max} above band ceiling {ceil}";
                }

                if (c.Currency is not null && !string.Equals(e.Salary.Currency, c.Currency, StringComparison.Ordinal))
                {
                    yield return $"salary currency {e.Salary.Currency} expected {c.Currency}";
                }
            }
        }
        else if (c.Salary is SalaryExpectation.Absent && e.Salary is not null)
        {
            yield return "expected no salary but one was present";
        }

        if (c.MaxTech is { } maxTech && e.Technologies.Count > maxTech)
        {
            yield return $"technologies count {e.Technologies.Count} exceeds band ceiling {maxTech}";
        }

        if (c.MinTech is { } minTech && e.Technologies.Count < minTech)
        {
            yield return $"technologies count {e.Technologies.Count} below band floor {minTech}";
        }
    }

    private static List<GoldenCase> LoadCorpus()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "golden-enrichments.yaml");
        var stream = new YamlStream();
        using (var reader = new StreamReader(path))
        {
            stream.Load(reader);
        }

        var root = (YamlSequenceNode)stream.Documents[0].RootNode;
        var cases = new List<GoldenCase>(root.Children.Count);
        foreach (var node in root.Children)
        {
            var m = (YamlMappingNode)node;
            cases.Add(new GoldenCase(
                Id: int.Parse(Scalar(m, "id"), CultureInfo.InvariantCulture),
                Why: Scalar(m, "why"),
                Raw: Scalar(m, "raw"),
                Parses: bool.Parse(Scalar(m, "parses")),
                FailReason: Optional(m, "fail"),
                Remote: OptionalBool(m, "remote"),
                Contractor: OptionalBool(m, "contractor"),
                AiUsageIn: Set(m, "aiUsageIn"),
                TimezoneIn: Set(m, "timezoneIn"),
                StageIn: Set(m, "stageIn"),
                Salary: ParseSalaryExpectation(Optional(m, "salary")),
                SalaryFloor: OptionalDecimal(m, "salaryFloor"),
                SalaryCeil: OptionalDecimal(m, "salaryCeil"),
                Currency: Optional(m, "currency"),
                MinReasons: OptionalInt(m, "minReasons") ?? 1,
                MinTech: OptionalInt(m, "minTech"),
                MaxTech: OptionalInt(m, "maxTech")));
        }

        return cases;
    }

    private static SalaryExpectation ParseSalaryExpectation(string? value) => value switch
    {
        "present" => SalaryExpectation.Present,
        "absent" => SalaryExpectation.Absent,
        _ => SalaryExpectation.Unspecified,
    };

    private static string Scalar(YamlMappingNode m, string key) =>
        ((YamlScalarNode)m.Children[new YamlScalarNode(key)]).Value!;

    private static string? Optional(YamlMappingNode m, string key) =>
        m.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode s ? s.Value : null;

    private static bool? OptionalBool(YamlMappingNode m, string key) =>
        Optional(m, key) is { } v ? bool.Parse(v) : null;

    private static int? OptionalInt(YamlMappingNode m, string key) =>
        Optional(m, key) is { } v ? int.Parse(v, CultureInfo.InvariantCulture) : null;

    private static decimal? OptionalDecimal(YamlMappingNode m, string key) =>
        Optional(m, key) is { } v ? decimal.Parse(v, CultureInfo.InvariantCulture) : null;

    private static List<string> Set(YamlMappingNode m, string key)
    {
        if (m.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlSequenceNode seq)
        {
            return seq.Children.OfType<YamlScalarNode>().Select(s => s.Value!).ToList();
        }

        return [];
    }

    private enum SalaryExpectation
    {
        Unspecified,
        Present,
        Absent,
    }

    private sealed record GoldenCase(
        int Id,
        string Why,
        string Raw,
        bool Parses,
        string? FailReason,
        bool? Remote,
        bool? Contractor,
        IReadOnlyList<string> AiUsageIn,
        IReadOnlyList<string> TimezoneIn,
        IReadOnlyList<string> StageIn,
        SalaryExpectation Salary,
        decimal? SalaryFloor,
        decimal? SalaryCeil,
        string? Currency,
        int MinReasons,
        int? MinTech,
        int? MaxTech);
}
