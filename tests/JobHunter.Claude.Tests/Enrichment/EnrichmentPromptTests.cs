using JobHunter.Claude.Enrichment;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Enrichment;

/// <summary>
/// T08: the prompt is a pure function of its inputs, and its rendering is snapshot-tested so a change is
/// visible in a diff and forces a <c>PromptVersion</c> bump (enrichment-schema §Versioning). Truncation
/// happens at a paragraph boundary and is reported on the rendered result.
/// </summary>
public sealed class EnrichmentPromptTests
{
    private static readonly string SnapshotDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "prompt");

    private static EnrichmentPromptInput Sample(string description) => new(
        CompanyName: "Acme AI",
        CanonicalDomain: "acme.ai",
        Title: "Senior Platform Engineer",
        LocationSummary: "Remote (EMEA)",
        PublishedSalary: "€90,000–€120,000",
        EmploymentType: "FullTime",
        Description: description);

    [Fact]
    public void The_prompt_version_is_stamped()
    {
        EnrichmentPrompt.PromptVersion.ShouldBe("enrich-v2");
    }

    [Fact]
    public void The_user_content_is_a_pure_function_of_its_inputs()
    {
        var a = EnrichmentPrompt.RenderUser(Sample("A short posting."));
        var b = EnrichmentPrompt.RenderUser(Sample("A short posting."));

        a.Content.ShouldBe(b.Content);
        a.WasTruncated.ShouldBeFalse();
    }

    [Fact]
    public void The_user_content_matches_the_recorded_snapshot()
    {
        var rendered = EnrichmentPrompt.RenderUser(Sample("We are hiring a platform engineer.\n\nYou will build internal tooling."));

        var snapshotPath = Path.Combine(SnapshotDir, "user-content.snapshot.txt");
        var expected = File.ReadAllText(snapshotPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        rendered.Content.Replace("\r\n", "\n", StringComparison.Ordinal).ShouldBe(expected);
    }

    [Fact]
    public void A_missing_published_salary_renders_as_none()
    {
        var rendered = EnrichmentPrompt.RenderUser(Sample("body") with { PublishedSalary = null });

        rendered.Content.ShouldContain("Published salary: none");
    }

    [Fact]
    public void An_oversized_description_is_truncated_at_a_paragraph_boundary()
    {
        var firstPara = new string('a', 11_990);
        var secondPara = new string('b', 500);
        var description = firstPara + "\n\n" + secondPara;

        var rendered = EnrichmentPrompt.RenderUser(Sample(description));

        rendered.WasTruncated.ShouldBeTrue();
        rendered.Content.ShouldContain(firstPara);
        rendered.Content.ShouldNotContain("bbbbb");
    }
}
