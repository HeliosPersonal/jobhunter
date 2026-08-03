using JobHunter.Claude.Enrichment;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;
using EnrichmentAggregate = JobHunter.Domain.Intelligence.Enrichment;

namespace JobHunter.Claude.Tests.Enrichment;

/// <summary>
/// T12: the Domain-port implementation that turns one batch item's raw tool-use JSON into a validated
/// <see cref="Enrichment"/> aggregate, or a recorded failure (enrichment-schema §Parsing rules). It wraps
/// the eight-step tolerant parser and maps the wire shape onto the aggregate — the one place the parser's
/// output becomes a domain object — so per-item isolation (QG-3), invariant 4 (a reason on every
/// enrichment) and the salary drop-not-reject rule (step 5) are all asserted here against fixture JSON.
/// </summary>
public sealed class EnrichmentResultParserTests
{
    private static readonly Guid EnrichmentId = Guid.Parse("00000000-0000-0000-0000-0000000000E1");
    private static readonly Guid JobId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000B1");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 3, 3, 0, 0, TimeSpan.Zero);

    private readonly EnrichmentResultParser _parser = new();

    private EnrichmentParseOutcome Parse(string? rawJson) =>
        _parser.Parse(new EnrichmentParseRequest(EnrichmentId, JobId, RunId, "enrich-v1", CreatedAt, rawJson));

    [Fact]
    public void A_well_formed_result_becomes_an_enrichment_stamped_with_its_identity()
    {
        var outcome = Parse(
            """
            {"isRemote":true,"isContractorFriendly":false,"timezoneBand":"EMEA","aiUsage":"High",
             "companyStage":"SeriesB","technologies":["Go","Kubernetes"],
             "reasons":["Posting states fully remote across EMEA","Builds LLM inference infrastructure"]}
            """);

        outcome.IsSuccess.ShouldBeTrue();
        var e = outcome.Enrichment!;
        e.Id.ShouldBe(EnrichmentId);
        e.JobId.ShouldBe(JobId);
        e.RunId.ShouldBe(RunId);
        e.PromptVersion.ShouldBe("enrich-v1");
        e.CreatedAt.ShouldBe(CreatedAt);
        e.IsRemote.ShouldBeTrue();
        e.TimezoneBand.ShouldBe(TimezoneBand.EMEA);
        e.AiUsage.ShouldBe(AiUsageLevel.High);
        e.CompanyStage.ShouldBe(CompanyStage.SeriesB);
        e.Technologies.ShouldBe(["Go", "Kubernetes"]);
        e.Reasons.Count.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void A_valid_salary_is_mapped_to_the_value_object()
    {
        var outcome = Parse(
            """
            {"isRemote":true,"isContractorFriendly":true,"timezoneBand":"EMEA","aiUsage":"Medium",
             "companyStage":"Growth","technologies":[],
             "salary":{"min":90000,"max":120000,"currency":"USD","period":"Year","confidence":0.8},
             "reasons":["Range published in the posting"]}
            """);

        outcome.IsSuccess.ShouldBeTrue();
        var salary = outcome.Enrichment!.Salary.ShouldNotBeNull();
        salary.Min.ShouldBe(90000m);
        salary.Max.ShouldBe(120000m);
        salary.Currency.ShouldBe("USD");
        salary.Period.ShouldBe(SalaryPeriod.Year);
        salary.Confidence.ShouldBe(0.8m);
    }

    [Fact]
    public void A_null_salary_is_a_legal_cannot_tell()
    {
        var outcome = Parse(
            """
            {"isRemote":false,"isContractorFriendly":false,"timezoneBand":"Unknown","aiUsage":"Unknown",
             "companyStage":"Unknown","technologies":[],"salary":null,
             "reasons":["No pay information in the posting"]}
            """);

        outcome.IsSuccess.ShouldBeTrue();
        outcome.Enrichment!.Salary.ShouldBeNull();
    }

    [Fact]
    public void An_inverted_salary_range_is_swapped_and_noted_as_an_anomaly()
    {
        var outcome = Parse(
            """
            {"isRemote":true,"isContractorFriendly":false,"timezoneBand":"EMEA","aiUsage":"Low",
             "companyStage":"Public","technologies":[],
             "salary":{"min":150000,"max":100000,"currency":"EUR","period":"Year","confidence":0.5},
             "reasons":["Range published"]}
            """);

        outcome.IsSuccess.ShouldBeTrue();
        outcome.Enrichment!.Salary!.Min.ShouldBe(100000m);
        outcome.Enrichment!.Salary!.Max.ShouldBe(150000m);
        outcome.Anomalies.ShouldContain(a => a.Contains("inverted"));
    }

    [Fact]
    public void An_unknown_currency_drops_the_salary_but_keeps_the_assessment()
    {
        var outcome = Parse(
            """
            {"isRemote":true,"isContractorFriendly":false,"timezoneBand":"EMEA","aiUsage":"Low",
             "companyStage":"Seed","technologies":["Rust"],
             "salary":{"min":80000,"max":90000,"currency":"ZZZ","period":"Year","confidence":0.4},
             "reasons":["Remote-first startup"]}
            """);

        outcome.IsSuccess.ShouldBeTrue();
        outcome.Enrichment!.Salary.ShouldBeNull();
        outcome.Enrichment!.Technologies.ShouldContain("Rust");
        outcome.Anomalies.ShouldContain(a => a.Contains("currency"));
    }

    [Fact]
    public void An_unrecognised_enum_degrades_to_unknown_rather_than_failing()
    {
        var outcome = Parse(
            """
            {"isRemote":true,"isContractorFriendly":false,"timezoneBand":"MARS","aiUsage":"Sentient",
             "companyStage":"Imaginary","technologies":[],
             "reasons":["Some reason"]}
            """);

        outcome.IsSuccess.ShouldBeTrue();
        outcome.Enrichment!.TimezoneBand.ShouldBe(TimezoneBand.Unknown);
        outcome.Enrichment!.AiUsage.ShouldBe(AiUsageLevel.Unknown);
        outcome.Enrichment!.CompanyStage.ShouldBe(CompanyStage.Unknown);
        outcome.Anomalies.Count.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void An_empty_reasons_list_is_a_failure_invariant_4()
    {
        var outcome = Parse(
            """
            {"isRemote":true,"isContractorFriendly":false,"timezoneBand":"EMEA","aiUsage":"Low",
             "companyStage":"Seed","technologies":[],"reasons":[]}
            """);

        outcome.IsSuccess.ShouldBeFalse();
        outcome.Enrichment.ShouldBeNull();
        outcome.FailureReason.ShouldNotBeNull().ShouldContain("reasons");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"isRemote\":\"yes\"}")]
    public void A_malformed_or_incomplete_payload_is_a_recorded_failure_not_a_throw(string? rawJson)
    {
        var outcome = Parse(rawJson);

        outcome.IsSuccess.ShouldBeFalse();
        outcome.FailureReason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Technologies_are_capped_at_the_aggregate_maximum()
    {
        var techs = string.Join(",", Enumerable.Range(0, 40).Select(i => $"\"tech{i}\""));
        var outcome = Parse(
            $$"""
            {"isRemote":true,"isContractorFriendly":false,"timezoneBand":"EMEA","aiUsage":"Low",
             "companyStage":"Seed","technologies":[{{techs}}],"reasons":["r"]}
            """);

        outcome.IsSuccess.ShouldBeTrue();
        outcome.Enrichment!.Technologies.Count.ShouldBeLessThanOrEqualTo(EnrichmentAggregate.MaxTechnologies);
    }
}
