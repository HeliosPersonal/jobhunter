using JobHunter.Claude.Enrichment;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Enrichment;

/// <summary>
/// T08: the tolerant parser against the fixture corpus (test-plan §Fixture corpus). Every case asserts the
/// eight parsing rules (enrichment-schema §Parsing rules): the happy path, malformed JSON, schema
/// violations, invariant-4 enforcement, enum degradation, salary swap/drop/clamp, and technology capping —
/// all without a single throw for a bad item (QG-3).
/// </summary>
public sealed class TolerantJsonParserTests
{
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "enrichment");

    private static string Load(string name) => File.ReadAllText(Path.Combine(FixtureDir, name));

    [Fact]
    public void A_valid_payload_parses_all_fields()
    {
        var outcome = TolerantJsonParser.Parse(Load("valid.json"));

        outcome.IsSuccess.ShouldBeTrue();
        var o = outcome.Output!;
        o.IsRemote.ShouldBeTrue();
        o.TimezoneBand.ShouldBe(TimezoneBand.EMEA);
        o.AiUsage.ShouldBe(AiUsageLevel.High);
        o.CompanyStage.ShouldBe(CompanyStage.SeriesB);
        o.RoleFamily.ShouldBe(RoleFamily.AiPlatform);
        o.AiSignals.BuildsAiInfra.ShouldBeTrue();
        o.AiSignals.BuildsAiProduct.ShouldBeFalse();
        o.AiSignals.UsesAiTooling.ShouldBeFalse();
        o.Technologies.ShouldBe(["Go", "Kubernetes", "PostgreSQL"]);
        o.Reasons.Count.ShouldBe(2);
        o.Salary!.Currency.ShouldBe("USD");
        o.Salary.Min.ShouldBe(120000m);
        o.Salary.Confidence.ShouldBe(0.7m);
    }

    [Fact]
    public void Malformed_json_is_a_parse_failure_not_a_throw()
    {
        var outcome = TolerantJsonParser.Parse(Load("truncated-json.json"));

        outcome.IsSuccess.ShouldBeFalse();
        outcome.FailureReason.ShouldNotBeNull();
        outcome.FailureReason.ShouldContain("malformed JSON");
    }

    [Fact]
    public void A_wrong_typed_required_field_is_a_parse_failure()
    {
        var outcome = TolerantJsonParser.Parse(Load("schema-violation.json"));

        outcome.IsSuccess.ShouldBeFalse();
        outcome.FailureReason.ShouldNotBeNull();
        outcome.FailureReason.ShouldContain("isRemote");
    }

    [Fact]
    public void An_empty_reasons_array_is_rejected_even_though_the_schema_forbids_it()
    {
        var outcome = TolerantJsonParser.Parse(Load("empty-reasons.json"));

        outcome.IsSuccess.ShouldBeFalse();
        outcome.FailureReason.ShouldNotBeNull();
        outcome.FailureReason.ShouldContain("invariant 4");
    }

    [Fact]
    public void An_unknown_enum_value_degrades_to_unknown_and_does_not_throw()
    {
        var outcome = TolerantJsonParser.Parse(Load("unknown-enum.json"));

        outcome.IsSuccess.ShouldBeTrue();
        var o = outcome.Output!;
        o.TimezoneBand.ShouldBe(TimezoneBand.Unknown);
        o.AiUsage.ShouldBe(AiUsageLevel.Unknown);
        o.CompanyStage.ShouldBe(CompanyStage.Unknown);
        outcome.Anomalies.Count.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void A_null_salary_is_legal_not_a_failure()
    {
        var outcome = TolerantJsonParser.Parse(Load("null-salary.json"));

        outcome.IsSuccess.ShouldBeTrue();
        outcome.Output!.Salary.ShouldBeNull();
    }

    [Fact]
    public void An_inverted_salary_range_is_swapped_and_the_anomaly_recorded()
    {
        var outcome = TolerantJsonParser.Parse(Load("inverted-salary.json"));

        outcome.IsSuccess.ShouldBeTrue();
        var salary = outcome.Output!.Salary!;
        salary.Min.ShouldBe(120000m);
        salary.Max.ShouldBe(180000m);
        outcome.Anomalies.ShouldContain(a => a.Contains("inverted", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_currency_drops_the_salary_but_keeps_the_rest()
    {
        var outcome = TolerantJsonParser.Parse(Load("unknown-currency.json"));

        outcome.IsSuccess.ShouldBeTrue();
        outcome.Output!.Salary.ShouldBeNull();
        outcome.Output.AiUsage.ShouldBe(AiUsageLevel.None);
        outcome.Anomalies.ShouldContain(a => a.Contains("currency", StringComparison.Ordinal));
    }

    [Fact]
    public void A_confidence_outside_the_unit_interval_is_clamped_not_rejected()
    {
        var outcome = TolerantJsonParser.Parse(Load("out-of-range-confidence.json"));

        outcome.IsSuccess.ShouldBeTrue();
        outcome.Output!.Salary!.Confidence.ShouldBe(1m);
        outcome.Output.Salary.Period.ShouldBe(SalaryPeriod.Month);
        outcome.Anomalies.ShouldContain(a => a.Contains("clamped", StringComparison.Ordinal));
    }

    [Fact]
    public void Technologies_are_capped_at_twenty_five()
    {
        var outcome = TolerantJsonParser.Parse(Load("oversized-technologies.json"));

        outcome.IsSuccess.ShouldBeTrue();
        outcome.Output!.Technologies.Count.ShouldBe(25);
    }

    [Fact]
    public void An_empty_payload_is_a_parse_failure()
    {
        TolerantJsonParser.Parse(null).IsSuccess.ShouldBeFalse();
        TolerantJsonParser.Parse("   ").IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void Absent_ai_signals_degrade_to_all_false_without_failing_the_item()
    {
        var outcome = TolerantJsonParser.Parse(
            """
            {"isRemote":true,"isContractorFriendly":false,"timezoneBand":"EMEA","aiUsage":"Low",
             "companyStage":"Seed","technologies":["Go"],"reasons":["No aiSignals object present"]}
            """);

        outcome.IsSuccess.ShouldBeTrue();
        var s = outcome.Output!.AiSignals;
        s.BuildsAiProduct.ShouldBeFalse();
        s.BuildsAiInfra.ShouldBeFalse();
        s.UsesAiTooling.ShouldBeFalse();
        s.IsResearch.ShouldBeFalse();
    }

    [Fact]
    public void An_ai_product_company_with_crud_work_resolves_uses_tooling_not_builds_product()
    {
        // TUNE-04 target/trap boundary: marketing says AI, the work is CRUD → usesAiTooling, never buildsAiProduct.
        var outcome = TolerantJsonParser.Parse(
            """
            {"isRemote":true,"isContractorFriendly":false,"timezoneBand":"AMER","aiUsage":"Low",
             "aiSignals":{"buildsAiProduct":false,"buildsAiInfra":false,"usesAiTooling":true,"isResearch":false},
             "companyStage":"SeriesB","roleFamily":"EnterpriseCrud","technologies":["C#"],
             "reasons":["Company markets an AI product but the work is standard CRUD API development"]}
            """);

        outcome.IsSuccess.ShouldBeTrue();
        var s = outcome.Output!.AiSignals;
        s.UsesAiTooling.ShouldBeTrue();
        s.BuildsAiProduct.ShouldBeFalse();
        s.BuildsAiInfra.ShouldBeFalse();
    }
}
