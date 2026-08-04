using System.Globalization;
using JobHunter.Application.Matching;
using JobHunter.Application.Ranking;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Profiles;
using Shouldly;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace JobHunter.Application.Tests.Ranking;

/// <summary>
/// F4's golden ranking set (T11, test-plan §The golden ranking set): the quality gate G10 / QG-1 / QG-3.
/// Fifty jobs judged against one fixed CV/Owner by the <em>real, pure</em> ranking chain — the factual
/// <see cref="PreMatchFilter"/> (T12), then <see cref="ScoreCalculator"/> and <see cref="SuppressionEvaluator"/>
/// (T07/T08) — with <strong>no LLM in the loop</strong>: each case records the deep tier's fit judgement as a
/// value, so the whole chain is deterministic and its output is a fact this test can assert, not a sample it
/// must tolerate.
///
/// <para>The assertions are on <strong>bands and relative order, never exact scores</strong> (test-plan): the
/// model is not deterministic and a test that pinned exact scores would be flaky and get disabled. So a change
/// to the match prompt, the schema or the ranking weights that moves a case out of its band — or reorders the
/// top five — fails the build, which is exactly what gate G10 is for. The set carries all ten of the difficult
/// cases the ranking is easy to get wrong (perfect stack / wrong seniority, a stretch, an adjacent stack, an
/// ideal role in the wrong timezone, a below-floor estimate, contract in and out of scope, an aged excellent
/// fit that must not be buried, a vague posting, and an unenriched job at the 0.85 confidence multiplier).</para>
/// </summary>
public sealed class GoldenRankingSetTests
{
    // Band boundaries (test-plan §The golden ranking set): excellent >= 75, good [55,75), marginal [40,55),
    // reject < 40. "na" is a job the factual filter excluded before any score was computed.
    private const decimal ExcellentFloor = 75m;
    private const decimal GoodFloor = 55m;
    private const decimal MarginalFloor = 40m;

    // The default anti-goal penalty factor (T15), matching RankingOptions.AntiGoalPenaltyFactor, so the golden
    // chain down-weights anti-goal roles exactly as the handler does with default options.
    private const decimal DefaultAntiGoalPenaltyFactor = 0.5m;

    // The default negative-family penalty factor (T17), matching RankingOptions.NegativeFamilyPenaltyFactor, and
    // the default negative set, so the golden chain applies the general off-target down-weight exactly as the
    // handler does with default options. No golden case currently uses a default-negative family, so this changes
    // no band today; it keeps the chain faithful so a future target-role-family slice (T19) inherits it for free.
    private const decimal DefaultNegativeFamilyPenaltyFactor = 0.5m;
    private static readonly IReadOnlySet<RoleFamily> NegativeRoleFamilies = RankingOptions.DefaultNegativeRoleFamilies;

    private static readonly DateTimeOffset Now = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    // The one fixed reference Owner the whole set is judged against: EMEA, Senior, a 100000 USD floor, open to
    // full-time and contract work. The floor is a down-weight, not a hard filter (opt-in off), so a below-floor
    // estimate is shown-but-lower rather than suppressed unless the pre-match filter excludes it as a fact.
    private static readonly Profile Owner = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000FF"), isActive: true, "Owner",
        salaryFloor: 100000m, salaryFloorCurrency: "USD", timezoneBand: TimezoneBand.EMEA,
        preferredCountries: ["UA", "DE"], employmentTypes: [EmploymentType.FullTime, EmploymentType.Contract],
        updatedAt: Now);

    private static readonly PreMatchSettings PreMatch =
        new(OwnerSeniority: Seniority.Senior, SeniorityFloorGap: 2, SalaryConfidenceThreshold: 0.80m,
            SeniorityFloorExemptStages: PreMatchOptions.DefaultEarlyStages);

    private static readonly List<GoldenCase> Corpus = LoadCorpus();

    [Fact]
    public void The_set_holds_fifty_jobs_and_covers_every_band()
    {
        Corpus.Count.ShouldBe(50);

        var bands = Corpus.Select(c => c.Band).ToHashSet();
        bands.ShouldBe(Enum.GetValues<Band>().ToHashSet());
    }

    [Fact]
    public void All_ten_difficult_cases_from_the_test_plan_are_covered()
    {
        // The ten rows of test-plan §The golden ranking set. If a case is renamed or dropped, this fails by
        // name rather than by an aggregate count — the difficult cases are the point of the set.
        string[] expected =
        [
            "perfect-stack-wrong-seniority", "perfect-stack-stretch-seniority", "adjacent-stack-same-domain",
            "ideal-role-wrong-timezone", "ideal-role-salary-below-floor", "contract-role-owner-open",
            "contract-role-owner-not-open", "excellent-fit-aged-still-top-ten", "vague-posting",
            "no-enrichment-confidence-multiplier",
        ];

        var present = Corpus.Where(c => c.HardCase is not null).Select(c => c.HardCase!).ToHashSet();
        present.ShouldBe(expected.ToHashSet());
    }

    [Fact]
    public void Every_case_lands_in_its_labelled_band_with_the_right_suppression_reason()
    {
        var mismatches = new List<string>();

        foreach (var testCase in Corpus)
        {
            var outcome = Judge(testCase);

            if (outcome.Band != testCase.Band)
            {
                mismatches.Add($"#{testCase.Id} expected {testCase.Band} but scored {outcome.Band} "
                    + $"({outcome.FinalScore:0.0})");
                continue;
            }

            if (outcome.SuppressedBy != testCase.SuppressedBy)
            {
                mismatches.Add($"#{testCase.Id} expected suppression '{testCase.SuppressedBy ?? "(shown)"}' "
                    + $"but got '{outcome.SuppressedBy ?? "(shown)"}'");
            }
        }

        mismatches.ShouldBeEmpty(
            "The golden ranking set drifted (gate G10): a prompt, schema or weight change moved a case out of "
            + "its band, or changed a suppression reason. Update the set in the same PR with a stated reason, or "
            + "revert the change: " + string.Join("; ", mismatches));
    }

    [Fact]
    public void The_top_five_shown_jobs_keep_their_expected_relative_order()
    {
        // Rank every job that is actually shown (not filtered, not suppressed) and take the head of the digest.
        var shown = Corpus
            .Select(c => (Case: c, Outcome: Judge(c)))
            .Where(x => x.Outcome is { Band: not Band.Na, SuppressedBy: null })
            .Select(x => (x.Case.Id, x.Outcome.Result!.Value))
            .ToList();

        var topFive = ScoreCalculator
            .Rank(shown.Select(x => x.Value))
            .Take(5)
            .Select(r => shown.First(x => x.Value.JobId == r.JobId).Id)
            .ToList();

        var expected = Corpus
            .Where(c => c.Top5Rank is not null)
            .OrderBy(c => c.Top5Rank!.Value)
            .Select(c => c.Id)
            .ToList();

        expected.Count.ShouldBe(5, "the set must pin all five head positions.");
        topFive.ShouldBe(expected,
            "The top-five relative order changed (gate G10): freshness must not bury fit, and a weight change "
            + "that reshuffles the head of the digest is caught here.");
    }

    [Fact]
    public void Freshness_tempers_but_never_buries_a_strong_fit()
    {
        // The aged-excellent case (test-plan) proves the freshness term cannot bury fit: a 20-day-old excellent
        // fit stays excellent and inside the ranked head, below only the fresh strong jobs.
        var aged = Corpus.Single(c => c.HardCase == "excellent-fit-aged-still-top-ten");
        Judge(aged).Band.ShouldBe(Band.Excellent);

        var ranked = Corpus
            .Select(c => (c.Id, Judge(c)))
            .Where(x => x.Item2 is { Band: not Band.Na, SuppressedBy: null })
            .OrderByDescending(x => x.Item2.FinalScore)
            .Select(x => x.Id)
            .ToList();

        ranked.IndexOf(aged.Id).ShouldBeLessThan(10, "an aged excellent fit must stay in the top ten.");
    }

    // ---- The pure judging chain, exactly as the ranking handler runs it, minus the persistence -----------

    private static Outcome Judge(GoldenCase testCase)
    {
        // 1. The factual gate. An excluded job never reaches the scorer, and its band is "na".
        var verdict = PreMatchFilter.Evaluate(testCase.Job, Owner, hasCurrentMatch: false, PreMatch);
        if (verdict.Excluded)
        {
            return new Outcome(Band.Na, $"prematch:{RuleSlug(verdict.Rule!.Value)}", FinalScore: 0m, Result: null);
        }

        // 2. The career-alignment component, computed by the same pure function the handler uses (T14). An
        //    unenriched job has no recorded classifications, so it degrades to the lowest-alignment defaults.
        var alignment = AlignmentCalculator.Calculate(testCase.AiUsage, testCase.RoleFamily);

        // 3. The career-policy down-weights, classified and applied exactly as the handler does with the default
        //    options: both penalty factors 0.50, both suppression opt-ins off. The anti-goal rule (T15) halves a
        //    role the Owner is deliberately leaving (low AI on the enterprise-CRUD family); the negative-family
        //    rule (T17) halves a role in the Owner's off-target set (research-adjacent / prompt-only by default).
        //    The two factors fold multiplicatively into one reconcilable career-policy multiplier (QG-1), and
        //    under the disjoint defaults at most one ever fires on a given role.
        var antiGoal = AntiGoalClassifier.Classify(testCase.AiUsage, testCase.RoleFamily);
        var negativeFamily = NegativeFamilyClassifier.Classify(testCase.RoleFamily, NegativeRoleFamilies);
        var careerPolicyMultiplier =
            (antiGoal.IsAntiGoal ? DefaultAntiGoalPenaltyFactor : 1.00m)
            * (negativeFamily.IsNegative ? DefaultNegativeFamilyPenaltyFactor : 1.00m);

        // 4. The pure linear scorer, with no preference model active (its weight renormalises away).
        var result = ScoreCalculator.Calculate(
            new MatchFacts(testCase.Job.JobId, testCase.ModelScore),
            alignment.Value,
            preference: null,
            testCase.Job.Enrichment is not null,
            firstSeenAt: Now.AddDays(-testCase.AgeDays),
            now: Now,
            RankingWeights.Default,
            careerPolicyMultiplier);

        // 5. The presentation rules. The salary floor, the anti-goal and the negative-family suppressions are all
        //    down-weights by default (opt-ins off), so only the below-threshold rule can suppress here.
        var reason = SuppressionEvaluator.Evaluate(
            result, testCase.Job.Enrichment?.EstimatedSalary, Owner, salaryFloorOptIn: false,
            antiGoal, antiGoalSuppressionOptIn: false,
            negativeFamily, negativeFamilySuppressionOptIn: false);

        var suppressedBy = reason is null ? null : "rank:threshold";
        return new Outcome(BandOf(result.FinalScore), suppressedBy, result.FinalScore, result);
    }

    private static Band BandOf(decimal finalScore) => finalScore switch
    {
        >= ExcellentFloor => Band.Excellent,
        >= GoodFloor => Band.Good,
        >= MarginalFloor => Band.Marginal,
        _ => Band.Reject,
    };

    private static string RuleSlug(PreMatchRule rule) => rule switch
    {
        PreMatchRule.Timezone => "timezone",
        PreMatchRule.EmploymentType => "employment_type",
        PreMatchRule.SeniorityFloor => "seniority_floor",
        PreMatchRule.SalaryFloor => "salary_floor",
        PreMatchRule.Lifecycle => "lifecycle",
        _ => throw new InvalidOperationException($"Unknown pre-match rule '{rule}'."),
    };

    private static List<GoldenCase> LoadCorpus()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "golden-ranking.yaml");
        var stream = new YamlStream();
        using (var reader = new StreamReader(path))
        {
            stream.Load(reader);
        }

        var root = (YamlSequenceNode)stream.Documents[0].RootNode;
        return root.Children.Cast<YamlMappingNode>().Select(ParseCase).ToList();
    }

    private static GoldenCase ParseCase(YamlMappingNode node)
    {
        var id = int.Parse(Scalar(node, "id"), CultureInfo.InvariantCulture);
        var band = ParseBand(Scalar(node, "band"));
        var suppressedBy = Optional(node, "suppressed_by");
        var top5 = Optional(node, "top5_rank") is { } r ? int.Parse(r, CultureInfo.InvariantCulture) : (int?)null;
        var hardCase = Optional(node, "hard_case");
        var modelScore = int.Parse(Scalar(node, "model_score"), CultureInfo.InvariantCulture);
        var ageDays = int.Parse(Scalar(node, "age_days"), CultureInfo.InvariantCulture);

        // The enrichment's recorded classifications, the alignment inputs (T14). Absent — as for an
        // unenriched job — they degrade to the lowest-alignment defaults, exactly as the scope query does.
        var aiUsage = Optional(node, "ai_usage") is { } ai
            ? Enum.Parse<AiUsageLevel>(ai)
            : AiUsageLevel.None;
        var roleFamily = Optional(node, "role_family") is { } role
            ? Enum.Parse<RoleFamily>(role)
            : RoleFamily.Other;

        var job = new MatchJobContent(
            Guid.Parse(string.Create(CultureInfo.InvariantCulture, $"00000000-0000-0000-0000-{id:D12}")),
            "Acme", "acme.com", "Backend Engineer",
            Optional(node, "seniority"), "Remote", "USD 120000-160000 / Year",
            Scalar(node, "employment_type"), "We build things.", ParseEnrichment(node, aiUsage, roleFamily));

        return new GoldenCase(id, band, suppressedBy, top5, hardCase, modelScore, ageDays, aiUsage, roleFamily, job);
    }

    private static MatchEnrichmentContent? ParseEnrichment(
        YamlMappingNode node, AiUsageLevel aiUsage, RoleFamily roleFamily)
    {
        if (!node.Children.TryGetValue(new YamlScalarNode("enrichment"), out var raw)
            || raw is not YamlMappingNode enrichment)
        {
            return null;
        }

        SalaryEstimate? salary = null;
        if (enrichment.Children.TryGetValue(new YamlScalarNode("salary"), out var salaryRaw)
            && salaryRaw is YamlMappingNode salaryNode)
        {
            salary = SalaryEstimate.TryCreate(
                decimal.Parse(Scalar(salaryNode, "min"), CultureInfo.InvariantCulture),
                decimal.Parse(Scalar(salaryNode, "max"), CultureInfo.InvariantCulture),
                Scalar(salaryNode, "currency"),
                SalaryPeriod.Year,
                decimal.Parse(Scalar(salaryNode, "confidence"), CultureInfo.InvariantCulture)).Value;
        }

        // RoleFamily is not part of the match prompt's enrichment block, so it rides on the case, not here;
        // AiUsage is, so it is set from the case's recorded classification for prompt-shape fidelity.
        _ = roleFamily;
        return new MatchEnrichmentContent(
            CompanyStage.SeriesB,
            IsRemote: bool.Parse(Scalar(enrichment, "is_remote")),
            Enum.Parse<TimezoneBand>(Scalar(enrichment, "band")),
            IsContractorFriendly: false,
            EstimatedSalary: salary,
            Technologies: ["C#", ".NET"],
            AiUsage: aiUsage);
    }

    private static Band ParseBand(string value) => value switch
    {
        "excellent" => Band.Excellent,
        "good" => Band.Good,
        "marginal" => Band.Marginal,
        "reject" => Band.Reject,
        "na" => Band.Na,
        _ => throw new InvalidOperationException($"Unknown golden band '{value}'."),
    };

    private static string Scalar(YamlMappingNode mapping, string key) =>
        ((YamlScalarNode)mapping.Children[new YamlScalarNode(key)]).Value!;

    private static string? Optional(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar
            ? scalar.Value
            : null;

    private enum Band
    {
        Excellent,
        Good,
        Marginal,
        Reject,
        Na,
    }

    private readonly record struct Outcome(Band Band, string? SuppressedBy, decimal FinalScore, ScoreResult? Result);

    private sealed record GoldenCase(
        int Id, Band Band, string? SuppressedBy, int? Top5Rank, string? HardCase,
        int ModelScore, int AgeDays, AiUsageLevel AiUsage, RoleFamily RoleFamily, MatchJobContent Job);
}
