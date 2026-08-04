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
/// F4's target-role-family slice of the golden ranking set (T19, test-plan §KPI Precision@10, gate G10).
/// The alignment work (TUNE-01/02/05/06) exists to stop fit-to-CV burying aspiration; this slice pins that
/// promise as a build gate. Each pair couples one genuine <em>target</em>-family (Tier-1) role that is only a
/// stretch on fit against one <em>off-target</em> role the model scores much higher on fit — an anti-goal
/// enterprise-CRUD role (T15) or an off-target-family role (T17) — and asserts the target still out-ranks the
/// off-target, by band and by relative order.
///
/// <para>The pairs are judged by the <strong>same real, pure chain</strong> as
/// <see cref="GoldenRankingSetTests"/> — <see cref="PreMatchFilter"/> (a deliberate no-op here: every role is
/// Senior / FullTime / EMEA-remote / enriched / just-seen, so nothing is factually excluded and the slice
/// isolates alignment + career-policy), then <see cref="AlignmentCalculator"/>, the anti-goal /
/// negative-family classifiers and <see cref="ScoreCalculator"/> — with the default options (both penalty
/// factors 0.50, both suppression opt-ins off). The assertions are on <strong>bands and relative order, never
/// exact scores</strong>: the slice fails the build only if a re-weighting lets a high-fit off-target role
/// out-rank a stretch Tier-1 role, which is exactly the regression the alignment work must never
/// reintroduce.</para>
/// </summary>
public sealed class GoldenTargetFamilySliceTests
{
    // Band boundaries, identical to the golden ranking set (test-plan): excellent >= 75, good [55,75),
    // marginal [40,55), reject < 40.
    private const decimal ExcellentFloor = 75m;
    private const decimal GoodFloor = 55m;
    private const decimal MarginalFloor = 40m;

    // The default career-policy penalty factors (T15/T17), matching RankingOptions, so the slice down-weights
    // exactly as the handler does with default options.
    private const decimal DefaultAntiGoalPenaltyFactor = 0.5m;
    private const decimal DefaultNegativeFamilyPenaltyFactor = 0.5m;
    private static readonly IReadOnlySet<RoleFamily> NegativeRoleFamilies = RankingOptions.DefaultNegativeRoleFamilies;

    private static readonly DateTimeOffset Now = new(2026, 8, 4, 6, 0, 0, TimeSpan.Zero);

    // The same fixed reference Owner as the golden ranking set: EMEA, Senior, a 100000 USD floor, open to
    // full-time and contract. No pair carries a salary estimate, so the floor never bites here.
    private static readonly Profile Owner = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000FF"), isActive: true, "Owner",
        salaryFloor: 100000m, salaryFloorCurrency: "USD", timezoneBand: TimezoneBand.EMEA,
        preferredCountries: ["UA", "DE"], employmentTypes: [EmploymentType.FullTime, EmploymentType.Contract],
        updatedAt: Now);

    private static readonly PreMatchSettings PreMatch =
        new(OwnerSeniority: Seniority.Senior, SeniorityFloorGap: 2, SalaryConfidenceThreshold: 0.80m,
            SeniorityFloorExemptStages: PreMatchOptions.DefaultEarlyStages);

    private static readonly List<SlicePair> Slice = LoadSlice();

    [Fact]
    public void The_slice_holds_at_least_ten_pairs_covering_both_off_target_kinds()
    {
        Slice.Count.ShouldBeGreaterThanOrEqualTo(10, "T19 requires >= 10 target-vs-off-target pairs.");

        var kinds = Slice.Select(p => p.OffTarget.Kind).ToHashSet();
        kinds.ShouldBe(new HashSet<OffTargetKind> { OffTargetKind.AntiGoal, OffTargetKind.NegativeFamily },
            "the slice must exercise both the anti-goal (T15) and the negative-family (T17) down-weights.");
    }

    [Fact]
    public void Every_off_target_role_is_the_stronger_raw_fit_so_the_pair_actually_tests_something()
    {
        // The premise that gives the slice meaning: the off-target role is always the BETTER model fit. A pair
        // where the target were also the higher fit would prove nothing about alignment overriding fit.
        var weakPremises = Slice
            .Where(p => p.OffTarget.ModelScore <= p.Target.ModelScore)
            .Select(p => $"#{p.Id} {p.Scenario}: off-target fit {p.OffTarget.ModelScore} "
                + $"is not above target fit {p.Target.ModelScore}")
            .ToList();

        weakPremises.ShouldBeEmpty(
            "every pair must set the off-target as the stronger raw fit, or it does not test alignment: "
            + string.Join("; ", weakPremises));
    }

    [Fact]
    public void A_stretch_target_family_role_out_ranks_a_higher_fit_off_target_role_in_every_pair()
    {
        var regressions = new List<string>();

        foreach (var pair in Slice)
        {
            var target = Judge(pair.Target);
            var offTarget = Judge(pair.OffTarget);

            // Bands must land where the slice recorded them (so a weight change that moves either side is
            // caught), and the target must land in a strictly better band than the off-target.
            if (target.Band != pair.Target.Band)
            {
                regressions.Add($"#{pair.Id} {pair.Scenario}: target expected {pair.Target.Band} "
                    + $"but scored {target.Band} ({target.FinalScore:0.0})");
            }

            if (offTarget.Band != pair.OffTarget.Band)
            {
                regressions.Add($"#{pair.Id} {pair.Scenario}: off-target expected {pair.OffTarget.Band} "
                    + $"but scored {offTarget.Band} ({offTarget.FinalScore:0.0})");
            }

            if (target.Band >= offTarget.Band)
            {
                // Band enum is ordered best-first (Excellent = 0), so a strictly better band is a smaller value.
                regressions.Add($"#{pair.Id} {pair.Scenario}: target band {target.Band} is not better than "
                    + $"off-target band {offTarget.Band}");
            }

            // And the ordering the digest would use: the stretch Tier-1 role must score above the high-fit
            // off-target role. This is the invariant TUNE-01/02/05/06 must never let regress.
            if (target.FinalScore <= offTarget.FinalScore)
            {
                regressions.Add($"#{pair.Id} {pair.Scenario}: a high-fit off-target role "
                    + $"({offTarget.FinalScore:0.0}) out-ranks the stretch Tier-1 role ({target.FinalScore:0.0})");
            }
        }

        regressions.ShouldBeEmpty(
            "The target-role-family slice failed (gate G10): a prompt, schema or weight change let a high-fit "
            + "off-target role out-rank a stretch target-family role, or moved a case out of its band. Fit is "
            + "burying aspiration again — fix the change or update the slice in the same PR with a stated "
            + "reason: " + string.Join("; ", regressions));
    }

    // ---- The pure judging chain, identical to GoldenRankingSetTests.Judge minus the na short-circuit -------
    // (every slice role is constructed to pass the factual filter, so pre-match never excludes one here).

    private static Outcome Judge(RoleCase role)
    {
        var verdict = PreMatchFilter.Evaluate(role.Job, Owner, hasCurrentMatch: false, PreMatch);
        verdict.Excluded.ShouldBeFalse(
            "the slice isolates alignment; a role must not be factually pre-match excluded.");

        var alignment = AlignmentCalculator.Calculate(role.AiUsage, role.RoleFamily);

        var antiGoal = AntiGoalClassifier.Classify(role.AiUsage, role.RoleFamily);
        var negativeFamily = NegativeFamilyClassifier.Classify(role.RoleFamily, NegativeRoleFamilies);
        var careerPolicyMultiplier =
            (antiGoal.IsAntiGoal ? DefaultAntiGoalPenaltyFactor : 1.00m)
            * (negativeFamily.IsNegative ? DefaultNegativeFamilyPenaltyFactor : 1.00m);

        var result = ScoreCalculator.Calculate(
            new MatchFacts(role.Job.JobId, role.ModelScore),
            alignment.Value,
            preference: null,
            role.Job.Enrichment is not null,
            firstSeenAt: Now,
            now: Now,
            RankingWeights.Default,
            careerPolicyMultiplier);

        return new Outcome(BandOf(result.FinalScore), result.FinalScore);
    }

    private static Band BandOf(decimal finalScore) => finalScore switch
    {
        >= ExcellentFloor => Band.Excellent,
        >= GoodFloor => Band.Good,
        >= MarginalFloor => Band.Marginal,
        _ => Band.Reject,
    };

    // ---- Loading -------------------------------------------------------------------------------------------

    private static List<SlicePair> LoadSlice()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "golden-target-family-slice.yaml");
        var stream = new YamlStream();
        using (var reader = new StreamReader(path))
        {
            stream.Load(reader);
        }

        var root = (YamlSequenceNode)stream.Documents[0].RootNode;
        return root.Children.Cast<YamlMappingNode>().Select(ParsePair).ToList();
    }

    private static SlicePair ParsePair(YamlMappingNode node)
    {
        var id = int.Parse(Scalar(node, "id"), CultureInfo.InvariantCulture);
        var scenario = Scalar(node, "scenario");
        var target = ParseRole((YamlMappingNode)node.Children[new YamlScalarNode("target")], id, isOffTarget: false);
        var offTarget = ParseRole((YamlMappingNode)node.Children[new YamlScalarNode("off_target")], id, isOffTarget: true);
        return new SlicePair(id, scenario, target, offTarget);
    }

    private static RoleCase ParseRole(YamlMappingNode node, int pairId, bool isOffTarget)
    {
        var roleFamily = Enum.Parse<RoleFamily>(Scalar(node, "role_family"));
        var aiUsage = Enum.Parse<AiUsageLevel>(Scalar(node, "ai_usage"));
        var modelScore = int.Parse(Scalar(node, "model_score"), CultureInfo.InvariantCulture);
        var band = ParseBand(Scalar(node, "band"));
        var kind = Optional(node, "kind") is { } k ? ParseKind(k) : OffTargetKind.None;

        // The two roles of a pair get distinct, stable job ids so Rank's tie-break never conflates them.
        var jobId = Guid.Parse(string.Create(
            CultureInfo.InvariantCulture, $"00000000-0000-0000-0000-{pairId:D9}{(isOffTarget ? 1 : 0):D3}"));

        var job = new MatchJobContent(
            jobId, "Acme", "acme.com", "Backend Engineer",
            "Senior", "Remote", "USD 120000-160000 / Year", "FullTime", "We build things.",
            new MatchEnrichmentContent(
                CompanyStage.SeriesB, IsRemote: true, TimezoneBand.EMEA, IsContractorFriendly: false,
                EstimatedSalary: null, Technologies: ["C#", ".NET"], AiUsage: aiUsage));

        return new RoleCase(roleFamily, aiUsage, modelScore, band, kind, job);
    }

    private static OffTargetKind ParseKind(string value) => value switch
    {
        "anti_goal" => OffTargetKind.AntiGoal,
        "negative_family" => OffTargetKind.NegativeFamily,
        _ => throw new InvalidOperationException($"Unknown off-target kind '{value}'."),
    };

    private static Band ParseBand(string value) => value switch
    {
        "excellent" => Band.Excellent,
        "good" => Band.Good,
        "marginal" => Band.Marginal,
        "reject" => Band.Reject,
        _ => throw new InvalidOperationException($"Unknown slice band '{value}'."),
    };

    private static string Scalar(YamlMappingNode mapping, string key) =>
        ((YamlScalarNode)mapping.Children[new YamlScalarNode(key)]).Value!;

    private static string? Optional(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar
            ? scalar.Value
            : null;

    // Best-first order (Excellent = 0) so "a strictly better band" is a strictly smaller enum value.
    private enum Band
    {
        Excellent,
        Good,
        Marginal,
        Reject,
    }

    private enum OffTargetKind
    {
        None,
        AntiGoal,
        NegativeFamily,
    }

    private readonly record struct Outcome(Band Band, decimal FinalScore);

    private sealed record RoleCase(
        RoleFamily RoleFamily, AiUsageLevel AiUsage, int ModelScore, Band Band, OffTargetKind Kind,
        MatchJobContent Job);

    private sealed record SlicePair(int Id, string Scenario, RoleCase Target, RoleCase OffTarget);
}
