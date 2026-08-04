using JobHunter.Application.Matching;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Profiles;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Matching;

/// <summary>
/// T12 / ADR-F4-0003: the pre-match filter's five <em>factual</em> rules, each asserted at its boundary. Every
/// rule is a fact drawn from the enrichment, the posting and the Profile — never a judgement — so each test
/// pins where the rule fires and, just as importantly, where it declines to (a Global band, an Unknown type, an
/// off-ladder title, a cross-currency estimate). The filter is pure, so these are plain value-in/value-out
/// assertions with no clock, no repository and no CV.
/// </summary>
public sealed class PreMatchFilterTests
{
    private static readonly PreMatchSettings Settings =
        new(OwnerSeniority: Seniority.Senior, SeniorityFloorGap: 2, SalaryConfidenceThreshold: 0.80m,
            SeniorityFloorExemptStages: PreMatchOptions.DefaultEarlyStages);

    // ---- Timezone ----------------------------------------------------------------------------------

    [Fact]
    public void A_definite_incompatible_band_that_is_not_remote_is_excluded_on_timezone()
    {
        var job = Job(enrichment: Enrichment(band: TimezoneBand.APAC, isRemote: false));

        var verdict = PreMatchFilter.Evaluate(job, ProfileEmea(), hasCurrentMatch: false, Settings);

        verdict.Excluded.ShouldBeTrue();
        verdict.Rule.ShouldBe(PreMatchRule.Timezone);
        verdict.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void An_incompatible_band_that_is_remote_passes_the_timezone_rule()
    {
        var job = Job(enrichment: Enrichment(band: TimezoneBand.APAC, isRemote: true));

        PreMatchFilter.Evaluate(job, ProfileEmea(), hasCurrentMatch: false, Settings)
            .Excluded.ShouldBeFalse();
    }

    [Theory]
    [InlineData(TimezoneBand.Global)]
    [InlineData(TimezoneBand.Unknown)]
    public void A_global_or_unknown_band_is_never_excluded_on_timezone(TimezoneBand band)
    {
        var job = Job(enrichment: Enrichment(band: band, isRemote: false));

        PreMatchFilter.Evaluate(job, ProfileEmea(), hasCurrentMatch: false, Settings)
            .Excluded.ShouldBeFalse();
    }

    [Fact]
    public void The_same_band_is_never_excluded_on_timezone()
    {
        var job = Job(enrichment: Enrichment(band: TimezoneBand.EMEA, isRemote: false));

        PreMatchFilter.Evaluate(job, ProfileEmea(), hasCurrentMatch: false, Settings)
            .Excluded.ShouldBeFalse();
    }

    [Fact]
    public void A_job_with_no_enrichment_is_never_excluded_on_timezone()
    {
        var job = Job(enrichment: null);

        PreMatchFilter.Evaluate(job, ProfileEmea(), hasCurrentMatch: false, Settings)
            .Excluded.ShouldBeFalse();
    }

    // ---- Employment type ---------------------------------------------------------------------------

    [Fact]
    public void A_known_type_the_owner_does_not_seek_is_excluded()
    {
        var job = Job(employmentType: "Contract");
        var profile = Profile(employmentTypes: [EmploymentType.FullTime]);

        var verdict = PreMatchFilter.Evaluate(job, profile, hasCurrentMatch: false, Settings);

        verdict.Excluded.ShouldBeTrue();
        verdict.Rule.ShouldBe(PreMatchRule.EmploymentType);
    }

    [Fact]
    public void A_sought_type_passes_the_employment_rule()
    {
        var job = Job(employmentType: "Contract");
        var profile = Profile(employmentTypes: [EmploymentType.FullTime, EmploymentType.Contract]);

        PreMatchFilter.Evaluate(job, profile, hasCurrentMatch: false, Settings)
            .Excluded.ShouldBeFalse();
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("Freelance-ish")]
    public void An_unknown_or_unrecognised_type_is_never_excluded(string employmentType)
    {
        var job = Job(employmentType: employmentType);
        var profile = Profile(employmentTypes: [EmploymentType.FullTime]);

        PreMatchFilter.Evaluate(job, profile, hasCurrentMatch: false, Settings)
            .Excluded.ShouldBeFalse();
    }

    // ---- Seniority floor ---------------------------------------------------------------------------

    [Fact]
    public void A_role_two_levels_below_the_owner_is_excluded_on_seniority()
    {
        var job = Job(seniority: "Junior"); // rung 0; Senior is rung 2 → gap 2

        var verdict = PreMatchFilter.Evaluate(job, ProfileEmea(), hasCurrentMatch: false, Settings);

        verdict.Excluded.ShouldBeTrue();
        verdict.Rule.ShouldBe(PreMatchRule.SeniorityFloor);
    }

    [Fact]
    public void A_role_one_level_below_the_owner_is_not_excluded_on_seniority()
    {
        var job = Job(seniority: "Mid"); // rung 1; Senior is rung 2 → gap 1

        PreMatchFilter.Evaluate(job, ProfileEmea(), hasCurrentMatch: false, Settings)
            .Excluded.ShouldBeFalse();
    }

    [Theory]
    [InlineData("Lead")]
    [InlineData("Manager")]
    public void An_off_ladder_title_is_never_excluded_on_seniority(string seniority)
    {
        var job = Job(seniority: seniority);

        PreMatchFilter.Evaluate(job, ProfileEmea(), hasCurrentMatch: false, Settings)
            .Excluded.ShouldBeFalse();
    }

    [Fact]
    public void An_untitled_posting_is_never_excluded_on_seniority()
    {
        var job = Job(seniority: null);

        PreMatchFilter.Evaluate(job, ProfileEmea(), hasCurrentMatch: false, Settings)
            .Excluded.ShouldBeFalse();
    }

    // ---- T18: the early-stage seniority-floor exemption --------------------------------------------

    [Theory]
    [InlineData(CompanyStage.Seed)]
    [InlineData(CompanyStage.SeriesA)]
    public void An_early_stage_role_below_the_floor_is_exempted_and_reaches_matching(CompanyStage stage)
    {
        // A Founding-Engineer / early-startup role the Owner explicitly wants: two rungs below on paper, but at
        // Seed/Series-A the ladder is erratic and the absolute gap is not a fact worth excluding on (T18).
        var job = Job(seniority: "Junior", enrichment: Enrichment(stage: stage));

        PreMatchFilter.Evaluate(job, ProfileEmea(), hasCurrentMatch: false, Settings)
            .Excluded.ShouldBeFalse();
    }

    [Fact]
    public void An_early_stage_role_still_faces_the_other_factual_rules()
    {
        // The exemption is narrow: it lifts only the seniority floor. An early-stage role in an incompatible,
        // non-remote timezone is still a factual mismatch and is excluded on timezone, not passed through.
        var job = Job(seniority: "Junior", enrichment: Enrichment(stage: CompanyStage.Seed,
            band: TimezoneBand.APAC, isRemote: false));

        var verdict = PreMatchFilter.Evaluate(job, ProfileEmea(), hasCurrentMatch: false, Settings);

        verdict.Excluded.ShouldBeTrue();
        verdict.Rule.ShouldBe(PreMatchRule.Timezone);
    }

    [Theory]
    [InlineData(CompanyStage.SeriesB)]
    [InlineData(CompanyStage.Public)]
    [InlineData(CompanyStage.Unknown)]
    public void A_non_early_stage_role_below_the_floor_is_still_excluded(CompanyStage stage)
    {
        // The behaviour for every non-exempt stage — including Unknown, where we have no evidence of an
        // early-stage exception — is unchanged: two rungs below is still a factual floor breach.
        var job = Job(seniority: "Junior", enrichment: Enrichment(stage: stage));

        var verdict = PreMatchFilter.Evaluate(job, ProfileEmea(), hasCurrentMatch: false, Settings);

        verdict.Excluded.ShouldBeTrue();
        verdict.Rule.ShouldBe(PreMatchRule.SeniorityFloor);
    }

    [Fact]
    public void An_early_stage_role_with_no_enrichment_stage_cannot_claim_the_exemption()
    {
        // Without an enrichment there is no stage fact, so the exemption cannot apply and the ordinary floor
        // still bites — the exemption is evidence-driven, never a default (mirrors the enrichment-absent rules).
        var job = Job(seniority: "Junior", enrichment: null);

        var verdict = PreMatchFilter.Evaluate(job, ProfileEmea(), hasCurrentMatch: false, Settings);

        verdict.Excluded.ShouldBeTrue();
        verdict.Rule.ShouldBe(PreMatchRule.SeniorityFloor);
    }

    [Fact]
    public void An_empty_exempt_set_reproduces_the_pre_T18_behaviour_for_early_stage_roles()
    {
        // The Owner can turn the exemption off entirely; then even a Seed-stage role two rungs below is excluded,
        // exactly as before T18.
        var settings = Settings with { SeniorityFloorExemptStages = new HashSet<CompanyStage>() };
        var job = Job(seniority: "Junior", enrichment: Enrichment(stage: CompanyStage.Seed));

        var verdict = PreMatchFilter.Evaluate(job, ProfileEmea(), hasCurrentMatch: false, settings);

        verdict.Excluded.ShouldBeTrue();
        verdict.Rule.ShouldBe(PreMatchRule.SeniorityFloor);
    }

    // ---- Salary floor ------------------------------------------------------------------------------

    [Fact]
    public void A_high_confidence_estimate_wholly_below_the_floor_is_excluded()
    {
        var job = Job(enrichment: Enrichment(salary: Estimate(min: 40000, max: 60000, "USD", confidence: 0.90m)));
        var profile = Profile(salaryFloor: 100000m, currency: "USD");

        var verdict = PreMatchFilter.Evaluate(job, profile, hasCurrentMatch: false, Settings);

        verdict.Excluded.ShouldBeTrue();
        verdict.Rule.ShouldBe(PreMatchRule.SalaryFloor);
    }

    [Fact]
    public void A_low_confidence_estimate_below_the_floor_never_bites()
    {
        var job = Job(enrichment: Enrichment(salary: Estimate(min: 40000, max: 60000, "USD", confidence: 0.50m)));
        var profile = Profile(salaryFloor: 100000m, currency: "USD");

        PreMatchFilter.Evaluate(job, profile, hasCurrentMatch: false, Settings)
            .Excluded.ShouldBeFalse();
    }

    [Fact]
    public void An_estimate_whose_top_reaches_the_floor_is_not_excluded()
    {
        var job = Job(enrichment: Enrichment(salary: Estimate(min: 80000, max: 120000, "USD", confidence: 0.95m)));
        var profile = Profile(salaryFloor: 100000m, currency: "USD");

        PreMatchFilter.Evaluate(job, profile, hasCurrentMatch: false, Settings)
            .Excluded.ShouldBeFalse();
    }

    [Fact]
    public void A_cross_currency_estimate_never_bites_the_salary_floor()
    {
        var job = Job(enrichment: Enrichment(salary: Estimate(min: 40000, max: 60000, "EUR", confidence: 0.95m)));
        var profile = Profile(salaryFloor: 100000m, currency: "USD");

        PreMatchFilter.Evaluate(job, profile, hasCurrentMatch: false, Settings)
            .Excluded.ShouldBeFalse();
    }

    [Fact]
    public void No_floor_set_means_the_salary_rule_never_fires()
    {
        var job = Job(enrichment: Enrichment(salary: Estimate(min: 40000, max: 60000, "USD", confidence: 0.95m)));

        PreMatchFilter.Evaluate(job, ProfileEmea(), hasCurrentMatch: false, Settings)
            .Excluded.ShouldBeFalse();
    }

    // ---- Lifecycle ---------------------------------------------------------------------------------

    [Fact]
    public void A_job_with_a_current_match_is_excluded_on_lifecycle()
    {
        var job = Job();

        var verdict = PreMatchFilter.Evaluate(job, ProfileEmea(), hasCurrentMatch: true, Settings);

        verdict.Excluded.ShouldBeTrue();
        verdict.Rule.ShouldBe(PreMatchRule.Lifecycle);
    }

    // ---- Precedence & pass -------------------------------------------------------------------------

    [Fact]
    public void A_clean_job_passes_every_rule()
    {
        var job = Job(enrichment: Enrichment(band: TimezoneBand.EMEA, isRemote: true));

        PreMatchFilter.Evaluate(job, ProfileEmea(), hasCurrentMatch: false, Settings)
            .Excluded.ShouldBeFalse();
    }

    [Fact]
    public void Timezone_takes_precedence_over_a_later_failing_rule()
    {
        // Both timezone (APAC, not remote) and lifecycle (current match) would exclude; timezone is first.
        var job = Job(enrichment: Enrichment(band: TimezoneBand.APAC, isRemote: false));

        var verdict = PreMatchFilter.Evaluate(job, ProfileEmea(), hasCurrentMatch: true, Settings);

        verdict.Rule.ShouldBe(PreMatchRule.Timezone);
    }

    [Fact]
    public void An_excluding_verdict_cannot_be_built_without_a_reason()
    {
        Should.Throw<ArgumentException>(() => PreMatchVerdict.Exclude(PreMatchRule.Timezone, "  "));
    }

    // ---- Builders ----------------------------------------------------------------------------------

    private static MatchJobContent Job(
        string? seniority = "Senior",
        string employmentType = "FullTime",
        MatchEnrichmentContent? enrichment = null) =>
        new(
            Guid.CreateVersion7(), "Acme", "acme.com", "Backend Engineer", seniority, "Remote — EMEA",
            "USD 120000-160000 / Year", employmentType, "We build things.",
            enrichment ?? Enrichment(band: TimezoneBand.EMEA, isRemote: true));

    private static MatchEnrichmentContent Enrichment(
        TimezoneBand band = TimezoneBand.EMEA,
        bool isRemote = true,
        SalaryEstimate? salary = null,
        CompanyStage stage = CompanyStage.SeriesB) =>
        new(
            stage, isRemote, band, IsContractorFriendly: false,
            EstimatedSalary: salary, Technologies: ["C#", ".NET"], AiUsage: AiUsageLevel.Medium);

    private static SalaryEstimate Estimate(decimal min, decimal max, string currency, decimal confidence) =>
        SalaryEstimate.TryCreate(min, max, currency, SalaryPeriod.Year, confidence).Value;

    private static Profile ProfileEmea() => Profile();

    private static Profile Profile(
        decimal? salaryFloor = null,
        string? currency = null,
        IReadOnlyList<EmploymentType>? employmentTypes = null,
        TimezoneBand timezoneBand = TimezoneBand.EMEA) =>
        new(
            Guid.CreateVersion7(), isActive: true, "Owner", salaryFloor, currency, timezoneBand,
            preferredCountries: ["UA", "DE"],
            employmentTypes: employmentTypes ?? [EmploymentType.FullTime, EmploymentType.Contract],
            updatedAt: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
}
