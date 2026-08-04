using JobHunter.Claude.Prompts;
using JobHunter.Domain.Reporting;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Prompts;

/// <summary>
/// T05: the digest-narrative prompt is a pure function of its <see cref="NarrativeInput"/>, and its rendering
/// is snapshot-tested so a change to the system prompt or the user template is visible in a diff and forces a
/// <c>PromptVersion</c> bump (F5 SAD §6.1, ADR-F5-0001). The user content is built <strong>only</strong> from
/// aggregate counts and one salary statistic — never the CV, a card reason or a job description — so the CV
/// still crosses exactly one boundary (F4's match prompt) and it is not this one.
/// </summary>
public sealed class DigestNarrativePromptTests
{
    private static readonly string SnapshotDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "narrative-prompt");

    private static NarrativeInput Sample(
        int totalNewJobs = 42,
        int strongMatches = 6,
        int cardCount = 5,
        decimal? avgSalaryUsd = 145000m,
        int suppressedCount = 11,
        int carriedOverCount = 3,
        int degradedSourceCount = 1) => new(
        totalNewJobs, strongMatches, cardCount, avgSalaryUsd, suppressedCount, carriedOverCount, degradedSourceCount);

    [Fact]
    public void The_prompt_version_is_stamped()
    {
        DigestNarrativePrompt.PromptVersion.ShouldBe("digest-narrative-v1");
    }

    [Fact]
    public void The_system_prompt_forbids_advice_ranking_history_and_the_owner()
    {
        DigestNarrativePrompt.System.ShouldContain("NOTHING about the reader");
        DigestNarrativePrompt.System.ShouldContain("never claim a comparison");
        DigestNarrativePrompt.System.ShouldContain("never tell the reader to apply");
    }

    [Fact]
    public void The_render_is_a_pure_function_of_its_inputs()
    {
        DigestNarrativePrompt.Render(Sample()).ShouldBe(DigestNarrativePrompt.Render(Sample()));
    }

    [Fact]
    public void The_rendered_prompt_matches_the_recorded_snapshot()
    {
        var rendered = DigestNarrativePrompt.Render(Sample());

        var snapshotPath = Path.Combine(SnapshotDir, "digest-narrative.snapshot.txt");
        var expected = File.ReadAllText(snapshotPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        rendered.Replace("\r\n", "\n", StringComparison.Ordinal).ShouldBe(expected);
    }

    [Fact]
    public void An_average_salary_renders_as_a_whole_dollar_figure()
    {
        DigestNarrativePrompt.Render(Sample(avgSalaryUsd: 132500m))
            .ShouldContain("Average advertised salary: $132500 USD");
    }

    [Fact]
    public void A_missing_average_salary_renders_as_none_rather_than_a_zero()
    {
        var rendered = DigestNarrativePrompt.Render(Sample(avgSalaryUsd: null));

        rendered.ShouldContain("Average advertised salary: none");
        rendered.ShouldNotContain("$0 USD");
    }

    [Fact]
    public void Every_count_is_stated_so_the_model_cannot_invent_one()
    {
        var rendered = DigestNarrativePrompt.Render(Sample(
            totalNewJobs: 7, strongMatches: 2, cardCount: 2, suppressedCount: 4,
            carriedOverCount: 1, degradedSourceCount: 2));

        rendered.ShouldContain("New roles discovered: 7");
        rendered.ShouldContain("Strong matches (shown): 2");
        rendered.ShouldContain("Cards presented: 2");
        rendered.ShouldContain("Scores suppressed with a reason: 4");
        rendered.ShouldContain("Items carried over from a missed batch: 1");
        rendered.ShouldContain("Sources degraded or quarantined: 2");
    }
}
