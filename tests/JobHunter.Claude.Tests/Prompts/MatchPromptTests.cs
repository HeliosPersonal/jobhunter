using JobHunter.Claude.Prompts;
using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Prompts;

/// <summary>
/// T04: the match prompt is a pure function of its inputs, and its rendering is snapshot-tested so a change
/// is visible in a diff and forces a <c>PromptVersion</c> bump (match-schema §Prompt). The CV is truncated
/// at a section boundary and the posting at a paragraph boundary, each reported on the rendered result. A
/// missing enrichment omits the enrichment lines entirely rather than filling them with <c>Unknown</c>
/// (AC-09). The CV and candidate preferences render into the stable cache prefix; nothing volatile
/// precedes the end of the CV block (match-schema §Prompt caching).
/// </summary>
public sealed class MatchPromptTests
{
    private static readonly string SnapshotDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "match-prompt");

    private static MatchEnrichmentFacts SampleEnrichment() => new(
        CompanyStage: "SeriesB",
        IsRemote: true,
        TimezoneBand: "EMEA",
        IsContractorFriendly: false,
        EstimatedSalary: "EUR 90000-120000",
        SalaryConfidence: 0.8m,
        Technologies: "Go, Kubernetes, PostgreSQL",
        AiUsage: "High");

    private static MatchPromptInput Sample(
        string cvText = "Senior Platform Engineer with seven years of Go and Kubernetes.",
        string description = "We are hiring a platform engineer.\n\nYou will build internal tooling.",
        bool withEnrichment = true,
        string? targetRoleFamilies = null,
        string? desiredAiUsageFloor = null,
        string? targetTitles = null) => new(
        CvText: cvText,
        SalaryFloor: 90000m,
        SalaryFloorCurrency: "EUR",
        OwnerTimezoneBand: "EMEA",
        EmploymentTypesOpenTo: "FullTime, Contract",
        // Career goal (T16): null by default so the no-goal render — the recorded snapshot — is unchanged.
        TargetRoleFamilies: targetRoleFamilies,
        DesiredAiUsageFloor: desiredAiUsageFloor,
        TargetTitles: targetTitles,
        CompanyName: "Acme AI",
        Title: "Senior Platform Engineer",
        Seniority: "Senior",
        LocationSummary: "Remote (EMEA)",
        EmploymentType: "FullTime",
        PublishedSalary: "€90,000–€120,000",
        Description: description,
        Enrichment: withEnrichment ? SampleEnrichment() : null);

    [Fact]
    public void The_prompt_version_is_stamped()
    {
        MatchPrompt.PromptVersion.ShouldBe("match-v2");
    }

    [Fact]
    public void The_system_prompt_is_stable_and_carries_the_calibration_rules()
    {
        MatchPrompt.System.ShouldContain("matchScore is fit, not desirability");
        MatchPrompt.System.ShouldContain("Be pessimistic");
    }

    [Fact]
    public void The_render_is_a_pure_function_of_its_inputs()
    {
        var a = MatchPrompt.Render(Sample());
        var b = MatchPrompt.Render(Sample());

        a.CvBlock.ShouldBe(b.CvBlock);
        a.RoleBlock.ShouldBe(b.RoleBlock);
        a.WasTruncated.ShouldBeFalse();
    }

    [Fact]
    public void The_rendered_prompt_matches_the_recorded_snapshot()
    {
        var rendered = MatchPrompt.Render(Sample());

        var combined = rendered.CvBlock + "\n<<<CACHE_BREAKPOINT>>>\n" + rendered.RoleBlock;
        var snapshotPath = Path.Combine(SnapshotDir, "match-prompt.snapshot.txt");
        var expected = File.ReadAllText(snapshotPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        combined.Replace("\r\n", "\n", StringComparison.Ordinal).ShouldBe(expected);
    }

    [Fact]
    public void The_cv_and_preferences_are_in_the_stable_cache_prefix_not_the_role_block()
    {
        var rendered = MatchPrompt.Render(Sample(cvText: "UNIQUE-CV-MARKER Go engineer."));

        rendered.CvBlock.ShouldContain("UNIQUE-CV-MARKER");
        rendered.CvBlock.ShouldContain("Salary floor: 90000 EUR");
        rendered.CvBlock.ShouldContain("Open to: FullTime, Contract");
        rendered.RoleBlock.ShouldNotContain("UNIQUE-CV-MARKER");
    }

    [Fact]
    public void The_cv_block_is_byte_identical_across_items_that_differ_only_in_the_role()
    {
        var itemA = MatchPrompt.Render(Sample(description: "Role A."));
        var itemB = MatchPrompt.Render(Sample(description: "Role B — quite different."));

        // Load-bearing for the shared cache prefix (match-schema §Prompt caching): the CV block is the
        // stable prefix and must not change per item, while the role block does.
        itemA.CvBlock.ShouldBe(itemB.CvBlock);
        itemA.RoleBlock.ShouldNotBe(itemB.RoleBlock);
    }

    [Fact]
    public void A_missing_salary_floor_renders_as_none()
    {
        var rendered = MatchPrompt.Render(Sample() with { SalaryFloor = null, SalaryFloorCurrency = null });

        rendered.CvBlock.ShouldContain("Salary floor: none");
    }

    [Fact]
    public void A_missing_enrichment_omits_the_enrichment_lines_rather_than_filling_unknown()
    {
        var rendered = MatchPrompt.Render(Sample(withEnrichment: false));

        rendered.RoleBlock.ShouldNotContain("Company stage:");
        rendered.RoleBlock.ShouldNotContain("Estimated salary:");
        rendered.RoleBlock.ShouldNotContain("Technologies:");
        rendered.RoleBlock.ShouldNotContain("AI usage:");
        rendered.RoleBlock.ShouldNotContain("Unknown");
        // The factual role header is still present.
        rendered.RoleBlock.ShouldContain("Company: Acme AI");
        rendered.RoleBlock.ShouldContain("Title: Senior Platform Engineer");
    }

    [Fact]
    public void A_present_enrichment_folds_its_facts_into_the_role_block()
    {
        var rendered = MatchPrompt.Render(Sample());

        rendered.RoleBlock.ShouldContain("Company stage: SeriesB");
        rendered.RoleBlock.ShouldContain("Estimated salary: EUR 90000-120000 (confidence 0.8)");
        rendered.RoleBlock.ShouldContain("Technologies: Go, Kubernetes, PostgreSQL");
        rendered.RoleBlock.ShouldContain("AI usage: High");
    }

    [Fact]
    public void An_oversized_cv_is_truncated_at_a_section_boundary()
    {
        var firstSection = new string('a', 7_990);
        var secondSection = new string('b', 500);
        var cv = firstSection + "\n\n" + secondSection;

        var rendered = MatchPrompt.Render(Sample(cvText: cv));

        rendered.CvWasTruncated.ShouldBeTrue();
        rendered.WasTruncated.ShouldBeTrue();
        rendered.CvBlock.ShouldContain(firstSection);
        rendered.CvBlock.ShouldNotContain("bbbbb");
    }

    [Fact]
    public void An_oversized_description_is_truncated_at_a_paragraph_boundary()
    {
        var firstPara = new string('a', 9_990);
        var secondPara = new string('b', 500);
        var description = firstPara + "\n\n" + secondPara;

        var rendered = MatchPrompt.Render(Sample(description: description));

        rendered.DescriptionWasTruncated.ShouldBeTrue();
        rendered.RoleBlock.ShouldContain(firstPara);
        rendered.RoleBlock.ShouldNotContain("bbbbb");
    }

    [Fact]
    public void A_missing_published_salary_and_seniority_render_as_placeholders()
    {
        var rendered = MatchPrompt.Render(Sample() with { PublishedSalary = null, Seniority = null });

        rendered.RoleBlock.ShouldContain("Published salary: none");
        rendered.RoleBlock.ShouldContain("Seniority: unspecified");
    }

    [Fact]
    public void A_null_cv_text_does_not_throw()
    {
        var rendered = MatchPrompt.Render(Sample() with { CvText = null! });

        rendered.CvWasTruncated.ShouldBeFalse();
    }

    // ---- T16: the Owner's career goal renders into the stable candidate block, only when stated ----

    [Fact]
    public void No_stated_goal_omits_the_goal_section_entirely()
    {
        var rendered = MatchPrompt.Render(Sample());

        rendered.CvBlock.ShouldNotContain("Career goal:");
        rendered.CvBlock.ShouldNotContain("Desired AI-usage floor:");
        rendered.CvBlock.ShouldNotContain("Target titles:");
    }

    [Fact]
    public void A_stated_goal_renders_the_directive_and_optional_lines_into_the_cache_prefix()
    {
        var rendered = MatchPrompt.Render(Sample(
            targetRoleFamilies: "AiPlatform, Platform",
            desiredAiUsageFloor: "Medium",
            targetTitles: "Staff Platform Engineer"));

        rendered.CvBlock.ShouldContain("Career goal: the candidate is deliberately targeting AiPlatform, Platform.");
        rendered.CvBlock.ShouldContain("Reward genuine alignment");
        rendered.CvBlock.ShouldContain("Desired AI-usage floor: Medium");
        rendered.CvBlock.ShouldContain("Target titles: Staff Platform Engineer");
        // The goal is a Profile fact, so it belongs in the stable prefix, never in the per-item role block.
        rendered.RoleBlock.ShouldNotContain("Career goal:");
    }

    [Fact]
    public void The_optional_goal_lines_are_each_omitted_when_absent()
    {
        var rendered = MatchPrompt.Render(Sample(targetRoleFamilies: "AiPlatform"));

        rendered.CvBlock.ShouldContain("Career goal: the candidate is deliberately targeting AiPlatform.");
        rendered.CvBlock.ShouldNotContain("Desired AI-usage floor:");
        rendered.CvBlock.ShouldNotContain("Target titles:");
    }

    [Fact]
    public void A_floor_or_titles_without_families_still_render_the_goal_section()
    {
        var rendered = MatchPrompt.Render(Sample(desiredAiUsageFloor: "High"));

        // Families absent: the directive falls back to a generic trajectory rather than naming a family.
        rendered.CvBlock.ShouldContain("targeting roles outside their current track");
        rendered.CvBlock.ShouldContain("Desired AI-usage floor: High");
    }

    [Fact]
    public void A_stated_goal_keeps_the_cv_block_stable_across_items_that_differ_only_in_the_role()
    {
        var itemA = MatchPrompt.Render(Sample(description: "Role A.", targetRoleFamilies: "AiPlatform"));
        var itemB = MatchPrompt.Render(Sample(description: "Role B.", targetRoleFamilies: "AiPlatform"));

        // The goal sits in the cache prefix, so adding it must not break the byte-identical-prefix guarantee.
        itemA.CvBlock.ShouldBe(itemB.CvBlock);
    }
}
