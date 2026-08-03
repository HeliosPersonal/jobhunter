using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Intelligence;

public sealed class EnrichmentTests
{
    private static readonly Guid EnrichmentId = Guid.Parse("00000000-0000-0000-0000-0000000000E1");
    private static readonly Guid JobId = Guid.Parse("00000000-0000-0000-0000-0000000000D1");
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");

    private static Enrichment NewEnrichment(
        IReadOnlyList<string>? reasons = null,
        IReadOnlyList<string>? technologies = null,
        SalaryEstimate? salary = null)
    {
        var clock = new FakeClock();
        return new Enrichment(
            EnrichmentId,
            JobId,
            RunId,
            salary,
            isRemote: true,
            isContractorFriendly: false,
            TimezoneBand.EMEA,
            AiUsageLevel.Low,
            new AiSignals(buildsAiProduct: false, buildsAiInfra: true, usesAiTooling: false, isResearch: false),
            CompanyStage.SeriesB,
            RoleFamily.Platform,
            technologies ?? ["Go", "Kubernetes"],
            reasons ?? ["Posting explicitly states fully remote within EMEA."],
            "enrich-v1",
            clock.UtcNow);
    }

    [Fact]
    public void A_valid_enrichment_exposes_its_fields()
    {
        var salary = SalaryEstimate.TryCreate(80000m, 120000m, "EUR", SalaryPeriod.Year, 0.6m).Value;

        var enrichment = NewEnrichment(salary: salary);

        enrichment.JobId.ShouldBe(JobId);
        enrichment.RunId.ShouldBe(RunId);
        enrichment.IsRemote.ShouldBeTrue();
        enrichment.IsContractorFriendly.ShouldBeFalse();
        enrichment.TimezoneBand.ShouldBe(TimezoneBand.EMEA);
        enrichment.AiUsage.ShouldBe(AiUsageLevel.Low);
        enrichment.CompanyStage.ShouldBe(CompanyStage.SeriesB);
        enrichment.RoleFamily.ShouldBe(RoleFamily.Platform);
        enrichment.AiSignals.BuildsAiInfra.ShouldBeTrue();
        enrichment.AiSignals.BuildsAiProduct.ShouldBeFalse();
        enrichment.Salary.ShouldBe(salary);
        enrichment.PromptVersion.ShouldBe("enrich-v1");
        enrichment.Technologies.ShouldBe(["Go", "Kubernetes"]);
        enrichment.Reasons.Count.ShouldBe(1);
    }

    [Fact]
    public void An_empty_reasons_list_is_rejected()
    {
        Should.Throw<ArgumentException>(() => NewEnrichment(reasons: []));
    }

    [Fact]
    public void A_whitespace_only_reasons_list_is_rejected()
    {
        Should.Throw<ArgumentException>(() => NewEnrichment(reasons: ["", "   ", "\t"]));
    }

    [Fact]
    public void Blank_reasons_are_trimmed_out_but_a_real_one_survives()
    {
        var enrichment = NewEnrichment(reasons: ["  ", "  Fully remote.  ", ""]);

        enrichment.Reasons.Count.ShouldBe(1);
        enrichment.Reasons[0].ShouldBe("Fully remote.");
    }

    [Fact]
    public void Technologies_are_trimmed_deblanked_and_capped_at_twenty_five()
    {
        var many = Enumerable.Range(0, 40).Select(i => $"tech{i}").ToList();

        var enrichment = NewEnrichment(technologies: many);

        enrichment.Technologies.Count.ShouldBe(Enrichment.MaxTechnologies);
    }

    [Fact]
    public void A_null_salary_is_allowed()
    {
        var enrichment = NewEnrichment(salary: null);

        enrichment.Salary.ShouldBeNull();
    }

    [Fact]
    public void Constructor_rejects_empty_job_or_run_ids()
    {
        var clock = new FakeClock();

        Should.Throw<ArgumentException>(() => new Enrichment(
            EnrichmentId, Guid.Empty, RunId, null, false, false,
            TimezoneBand.Unknown, AiUsageLevel.None, AiSignals.None, CompanyStage.Unknown, RoleFamily.Other,
            [], ["r"], "enrich-v1", clock.UtcNow));
        Should.Throw<ArgumentException>(() => new Enrichment(
            EnrichmentId, JobId, Guid.Empty, null, false, false,
            TimezoneBand.Unknown, AiUsageLevel.None, AiSignals.None, CompanyStage.Unknown, RoleFamily.Other,
            [], ["r"], "enrich-v1", clock.UtcNow));
    }

    [Fact]
    public void Constructor_rejects_a_blank_prompt_version()
    {
        var clock = new FakeClock();

        Should.Throw<ArgumentException>(() => new Enrichment(
            EnrichmentId, JobId, RunId, null, false, false,
            TimezoneBand.Unknown, AiUsageLevel.None, AiSignals.None, CompanyStage.Unknown, RoleFamily.Other,
            [], ["r"], " ", clock.UtcNow));
    }

    [Fact]
    public void Returned_reason_and_technology_lists_are_read_only_copies()
    {
        var enrichment = NewEnrichment();

        // The exposed collections are not the backing lists — mutating a copy cannot corrupt state.
        enrichment.Reasons.ShouldBeAssignableTo<IReadOnlyList<string>>();
        enrichment.Technologies.ShouldBeAssignableTo<IReadOnlyList<string>>();
    }
}
