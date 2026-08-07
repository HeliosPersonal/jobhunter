using JobHunter.Claude.Prompts;
using JobHunter.Domain.Research;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Prompts;

/// <summary>
/// T06: the synthesis prompt is a pure function of its <see cref="ResearchPromptInput"/>, and its rendering
/// is snapshot-tested so a change to the system prompt or the user template is visible in a diff and forces a
/// <c>PromptVersion</c> bump (research-schema §Prompt). The user content is built <strong>only</strong> from
/// the fetched documents — no company knowledge is injected from anywhere else — and the categories with no
/// documents are listed explicitly, which is the single most effective guard against the model filling a gap
/// from memory (research-schema §Prompt note).
/// </summary>
public sealed class ResearchSynthesisPromptTests
{
    private static readonly string SnapshotDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "research-prompt");

    private static readonly DateTimeOffset Observed = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

    private static ResearchPromptInput Sample(
        IReadOnlyList<CategorisedDocument>? documents = null,
        IReadOnlyList<ResearchCategory>? emptyCategories = null) => new(
        DisplayName: "Acme AI",
        CanonicalDomain: "acme.ai",
        Documents: documents ??
        [
            new CategorisedDocument(ResearchCategory.EngineeringBlog,
                new FetchedDocument("https://acme.ai/blog/scaling", "Scaling our platform", "We moved to Rust for the ingest path.", Observed)),
            new CategorisedDocument(ResearchCategory.OpenSource,
                new FetchedDocument("https://api.github.com/orgs/acme/repos", "Acme AI on GitHub", "acme-core [Rust] 1200 stars", Observed)),
        ],
        EmptyCategories: emptyCategories ?? [ResearchCategory.Reviews, ResearchCategory.InterviewProcess]);

    [Fact]
    public void The_prompt_version_is_stamped()
    {
        // Bumped to v2 when the optional firmographic fields (stage/employeeBand) were added to the schema
        // and the system prompt gained the rule that classifies them strictly from the fetched text.
        ResearchSynthesisPrompt.PromptVersion.ShouldBe("research-v2");
    }

    [Fact]
    public void The_system_prompt_states_the_only_from_documents_rule()
    {
        ResearchSynthesisPrompt.System.ShouldContain("summariser, not an expert");
        ResearchSynthesisPrompt.System.ShouldContain("If the documents do not say it, it does not exist");
        ResearchSynthesisPrompt.System.ShouldContain("copied verbatim");
        ResearchSynthesisPrompt.System.ShouldContain("isWarning");
    }

    [Fact]
    public void The_system_prompt_classifies_firmographics_only_from_the_documents()
    {
        // AC-10: the model may set stage/employeeBand, but only when the documents support it — the same
        // no-memory rule as the claims, so a firmographic guess never reaches the Company aggregate.
        ResearchSynthesisPrompt.System.ShouldContain("stage");
        ResearchSynthesisPrompt.System.ShouldContain("employeeBand");
        ResearchSynthesisPrompt.System.ShouldContain("Omit");
    }

    [Fact]
    public void The_user_content_is_a_pure_function_of_its_inputs()
    {
        var a = ResearchSynthesisPrompt.RenderUser(Sample());
        var b = ResearchSynthesisPrompt.RenderUser(Sample());

        a.ShouldBe(b);
    }

    [Fact]
    public void The_user_content_matches_the_recorded_snapshot()
    {
        var rendered = ResearchSynthesisPrompt.RenderUser(Sample());

        var snapshotPath = Path.Combine(SnapshotDir, "user-content.snapshot.txt");
        var expected = File.ReadAllText(snapshotPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        rendered.Replace("\r\n", "\n", StringComparison.Ordinal).ShouldBe(expected);
    }

    [Fact]
    public void Each_document_is_numbered_and_carries_its_source_url_category_date_and_title()
    {
        var rendered = ResearchSynthesisPrompt.RenderUser(Sample(documents:
        [
            new CategorisedDocument(ResearchCategory.EngineeringBlog,
                new FetchedDocument("https://acme.ai/blog/scaling", "Scaling our platform", "body text", Observed)),
        ]));

        rendered.ShouldContain("[1]");
        rendered.ShouldContain("sourceUrl: https://acme.ai/blog/scaling");
        rendered.ShouldContain("category: EngineeringBlog");
        rendered.ShouldContain("observed: 2026-08-01");
        rendered.ShouldContain("title: Scaling our platform");
        rendered.ShouldContain("body text");
    }

    [Fact]
    public void Categories_with_no_documents_are_listed_explicitly()
    {
        var rendered = ResearchSynthesisPrompt.RenderUser(Sample(
            emptyCategories: [ResearchCategory.Funding, ResearchCategory.Layoffs]));

        rendered.ShouldContain("Categories with no documents found: Funding, Layoffs");
    }

    [Fact]
    public void No_empty_categories_renders_the_absence_line_as_none()
    {
        var rendered = ResearchSynthesisPrompt.RenderUser(Sample(emptyCategories: []));

        rendered.ShouldContain("Categories with no documents found: none");
    }

    [Fact]
    public void Only_the_supplied_documents_appear_no_company_knowledge_is_injected()
    {
        // A famous company whose real facts the model "knows" — the render must carry nothing but the one
        // thin document, so a sparse input can only ever produce a sparse dossier (QG-2).
        var rendered = ResearchSynthesisPrompt.RenderUser(new ResearchPromptInput(
            DisplayName: "OpenAI",
            CanonicalDomain: "openai.com",
            Documents: [new CategorisedDocument(ResearchCategory.InterviewProcess,
                new FetchedDocument("https://openai.com/careers", "Careers", "We are hiring engineers.", Observed))],
            EmptyCategories: [ResearchCategory.Funding, ResearchCategory.News]));

        rendered.ShouldContain("We are hiring engineers.");
        rendered.ShouldNotContain("ChatGPT");
        rendered.ShouldNotContain("GPT-4");
        rendered.ShouldNotContain("Sam Altman");
    }

    [Fact]
    public void The_document_text_is_capped_so_one_huge_document_cannot_blow_the_budget()
    {
        var huge = new string('x', 40_000);
        var rendered = ResearchSynthesisPrompt.RenderUser(Sample(documents:
        [
            new CategorisedDocument(ResearchCategory.EngineeringBlog,
                new FetchedDocument("https://acme.ai/blog", "Blog", huge, Observed)),
        ]));

        // Capped at the per-document ceiling; the whole 40k never reaches the prompt.
        rendered.Length.ShouldBeLessThan(huge.Length);
        rendered.ShouldContain(new string('x', ResearchSynthesisPrompt.MaxDocumentChars));
        rendered.ShouldNotContain(new string('x', ResearchSynthesisPrompt.MaxDocumentChars + 1));
    }
}
