using System.Globalization;
using JobHunter.Application.Matching;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Profiles;
using Shouldly;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace JobHunter.Application.Tests.Matching;

/// <summary>
/// F4's pre-match reference corpus (T12, ADR-F4-0003, test-plan §The pre-match reference corpus): the labelled
/// set that pins the filter's factual decisions <em>and</em> its calibration. Every case is judged by the
/// <em>real</em> <see cref="PreMatchFilter"/> against one fixed Owner profile with the default settings — the
/// filter is pure, so no clock, repository or CV is involved. Two things must hold, or the build fails:
///
/// <list type="bullet">
/// <item><description>every case reaches its labelled verdict, and each exclusion fires the labelled rule
/// (so a mislabelled or drifted rule is caught by name, not by an aggregate count);</description></item>
/// <item><description>the pass rate lands in the 35–50% band the PRD calls healthy — a filter that passes
/// everything wastes the deep tier, one that passes too little hides good jobs (calibration).</description></item>
/// </list>
/// </summary>
public sealed class PreMatchCorpusTests
{
    private const double MinPassRate = 0.35;
    private const double MaxPassRate = 0.50;

    // The one fixed reference Owner and settings the whole corpus is judged against.
    private static readonly Profile Owner = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000FF"), isActive: true, "Owner",
        salaryFloor: 100000m, salaryFloorCurrency: "USD", timezoneBand: TimezoneBand.EMEA,
        preferredCountries: ["UA", "DE"], employmentTypes: [EmploymentType.FullTime],
        updatedAt: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));

    private static readonly PreMatchSettings Settings =
        new(OwnerSeniority: Seniority.Senior, SeniorityFloorGap: 2, SalaryConfidenceThreshold: 0.80m,
            SeniorityFloorExemptStages: PreMatchOptions.DefaultEarlyStages);

    private static readonly List<CorpusCase> Corpus = LoadCorpus();

    [Fact]
    public void The_corpus_covers_every_rule_and_a_clean_pass()
    {
        // A corpus that never exercises a rule cannot certify it. Each of the five rules, plus a pass, appears.
        var excludedRules = Corpus.Where(c => c.Expect == Verdict.Exclude).Select(c => c.Rule!.Value).ToHashSet();
        excludedRules.ShouldBe(Enum.GetValues<PreMatchRule>().ToHashSet());
        Corpus.ShouldContain(c => c.Expect == Verdict.Pass);
    }

    [Fact]
    public void Every_case_reaches_its_labelled_verdict_and_rule()
    {
        var mismatches = new List<string>();
        foreach (var testCase in Corpus)
        {
            var verdict = PreMatchFilter.Evaluate(testCase.Job, Owner, testCase.HasCurrentMatch, Settings);

            if (testCase.Expect == Verdict.Pass)
            {
                if (verdict.Excluded)
                {
                    mismatches.Add($"#{testCase.Id} expected pass but was excluded on {verdict.Rule} ({testCase.Why})");
                }

                continue;
            }

            if (!verdict.Excluded)
            {
                mismatches.Add($"#{testCase.Id} expected exclude:{testCase.Rule} but passed ({testCase.Why})");
            }
            else if (verdict.Rule != testCase.Rule)
            {
                mismatches.Add($"#{testCase.Id} expected rule {testCase.Rule} but fired {verdict.Rule} ({testCase.Why})");
            }
        }

        mismatches.ShouldBeEmpty(
            "The pre-match filter drifted from the labelled corpus: " + string.Join("; ", mismatches));
    }

    [Fact]
    public void Every_exclusion_carries_a_reason()
    {
        // Invariant 11 at the corpus scale: not one excluded case reaches a verdict without a reason.
        var reasonless = Corpus
            .Where(c => c.Expect == Verdict.Exclude)
            .Where(c => string.IsNullOrWhiteSpace(
                PreMatchFilter.Evaluate(c.Job, Owner, c.HasCurrentMatch, Settings).Reason))
            .Select(c => $"#{c.Id}")
            .ToList();

        reasonless.ShouldBeEmpty("Excluded cases with no reason: " + string.Join(", ", reasonless));
    }

    [Fact]
    public void The_pass_rate_lands_in_the_healthy_calibration_band()
    {
        var passes = Corpus.Count(c =>
            !PreMatchFilter.Evaluate(c.Job, Owner, c.HasCurrentMatch, Settings).Excluded);
        var rate = (double)passes / Corpus.Count;

        rate.ShouldBeInRange(
            MinPassRate,
            MaxPassRate,
            $"Pass rate {rate:P0} ({passes}/{Corpus.Count}) is outside the healthy 35–50% band; the filter is "
            + "mis-calibrated — too permissive wastes the deep tier, too strict hides good jobs.");
    }

    private static List<CorpusCase> LoadCorpus()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "pre-match-corpus.yaml");
        var stream = new YamlStream();
        using (var reader = new StreamReader(path))
        {
            stream.Load(reader);
        }

        var root = (YamlSequenceNode)stream.Documents[0].RootNode;
        var cases = new List<CorpusCase>(root.Children.Count);
        foreach (var node in root.Children.Cast<YamlMappingNode>())
        {
            var expect = ParseVerdict(Scalar(node, "expect"));
            cases.Add(new CorpusCase(
                int.Parse(Scalar(node, "id"), CultureInfo.InvariantCulture),
                expect,
                expect == Verdict.Exclude ? ParseRule(Scalar(node, "rule")) : null,
                Scalar(node, "why"),
                ParseBool(OptionalScalar(node, "has_current_match")),
                ParseJob(node)));
        }

        return cases;
    }

    private static MatchJobContent ParseJob(YamlMappingNode node)
    {
        MatchEnrichmentContent? enrichment = null;
        if (node.Children.TryGetValue(new YamlScalarNode("enrichment"), out var enrichmentNode)
            && enrichmentNode is YamlMappingNode enrichmentMapping)
        {
            enrichment = ParseEnrichment(enrichmentMapping);
        }

        return new MatchJobContent(
            Guid.CreateVersion7(), "Acme", "acme.com", "Backend Engineer",
            OptionalScalar(node, "seniority"), "Remote", "USD 120000-160000 / Year",
            Scalar(node, "employment_type"), "We build things.", enrichment);
    }

    private static MatchEnrichmentContent ParseEnrichment(YamlMappingNode node)
    {
        SalaryEstimate? salary = null;
        if (node.Children.TryGetValue(new YamlScalarNode("salary"), out var salaryNode)
            && salaryNode is YamlMappingNode salaryMapping)
        {
            salary = SalaryEstimate.TryCreate(
                decimal.Parse(Scalar(salaryMapping, "min"), CultureInfo.InvariantCulture),
                decimal.Parse(Scalar(salaryMapping, "max"), CultureInfo.InvariantCulture),
                Scalar(salaryMapping, "currency"),
                SalaryPeriod.Year,
                decimal.Parse(Scalar(salaryMapping, "confidence"), CultureInfo.InvariantCulture)).Value;
        }

        return new MatchEnrichmentContent(
            CompanyStage.SeriesB,
            ParseBool(OptionalScalar(node, "is_remote")),
            Enum.Parse<TimezoneBand>(Scalar(node, "band")),
            IsContractorFriendly: false,
            EstimatedSalary: salary,
            Technologies: ["C#", ".NET"],
            AiUsage: AiUsageLevel.Medium);
    }

    private static Verdict ParseVerdict(string value) => value switch
    {
        "pass" => Verdict.Pass,
        "exclude" => Verdict.Exclude,
        _ => throw new InvalidOperationException($"Unknown corpus verdict '{value}'."),
    };

    private static PreMatchRule ParseRule(string value) => value switch
    {
        "timezone" => PreMatchRule.Timezone,
        "employment_type" => PreMatchRule.EmploymentType,
        "seniority_floor" => PreMatchRule.SeniorityFloor,
        "salary_floor" => PreMatchRule.SalaryFloor,
        "lifecycle" => PreMatchRule.Lifecycle,
        _ => throw new InvalidOperationException($"Unknown corpus rule '{value}'."),
    };

    private static bool ParseBool(string? value) =>
        value is not null && bool.Parse(value);

    private static string Scalar(YamlMappingNode mapping, string key) =>
        ((YamlScalarNode)mapping.Children[new YamlScalarNode(key)]).Value!;

    private static string? OptionalScalar(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar
            ? scalar.Value
            : null;

    private enum Verdict
    {
        Pass,
        Exclude,
    }

    private sealed record CorpusCase(
        int Id, Verdict Expect, PreMatchRule? Rule, string Why, bool HasCurrentMatch, MatchJobContent Job);
}
