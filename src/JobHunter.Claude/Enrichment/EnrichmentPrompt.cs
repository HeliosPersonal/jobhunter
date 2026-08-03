using System.Globalization;

namespace JobHunter.Claude.Enrichment;

/// <summary>
/// The versioned enrichment prompt (enrichment-schema §Prompt). The system prompt is a constant; the user
/// content is a pure function of <see cref="EnrichmentPromptInput"/>, so a change to either is visible in a
/// snapshot diff and bumps <see cref="PromptVersion"/> (which is stamped on every batch and enrichment row,
/// so a quality regression is attributable to a specific change — AC-11). The description is truncated at a
/// paragraph boundary, never mid-sentence, and whether it was truncated is reported so a poor assessment can
/// be checked against it.
/// </summary>
public static class EnrichmentPrompt
{
    /// <summary>Bump whenever the system prompt, the user template, the schema or a parsing rule changes.</summary>
    public const string PromptVersion = "enrich-v2";

    /// <summary>The posting text is truncated to this many characters at a paragraph boundary.</summary>
    public const int MaxDescriptionChars = 12_000;

    public const string System =
        """
        You assess software engineering job postings. You extract facts and make calibrated estimates about
        the ROLE — never about any particular candidate.

        Rules:
        - Base every field on the posting text. Do not invent a salary a posting does not support.
        - "Remote" means the role can be performed remotely long-term, not "remote during onboarding" and
          not "remote within 50km of the office".
        - "Contractor friendly" requires positive evidence: B2B, contract, freelance, consultant, or an
          explicit statement. Silence means false.
        - Timezone band is where the role expects overlap, which is often not where the company is.
        - AI usage is how much the ENGINEERING work involves building with or on AI systems. A company that
          sells an AI product but whose posting describes CRUD work is Low.
        - Company stage: only from evidence in the posting (funding mentions, size statements, "public
          company", "early stage"). Otherwise Unknown.
        - Role family: classify by the WORK the posting describes, never by the title string. A posting
          titled "AI Engineer" whose responsibilities are ordinary line-of-business CRUD is EnterpriseCrud,
          not AiPlatform. A "Senior Software Engineer" building inference/serving infrastructure is
          AiPlatform. AiPlatform is building the platform AI runs on; AiApplications is building product
          features on top of AI; Platform is general infrastructure not centred on AI; ForwardDeployed is
          customer-embedded solutions work; FoundingEng is broad early-stage ownership; BackendGeneric,
          Frontend, Fullstack, DevOpsSRE, MlResearch, DataScience and PromptEng are as named. Use Other only
          when the described work genuinely fits none of these. The reason for the family must quote or
          paraphrase the responsibilities, not the title.
        - Every reason must be specific and quote or paraphrase the posting. "Good role" is not a reason.
        - If you cannot tell, say Unknown or null. A confident wrong answer is worse than an honest gap.
        """;

    /// <summary>
    /// Renders the per-item user content. Pure — no clock, no state — so the rendering is snapshot-tested.
    /// The returned <see cref="RenderedPrompt.WasTruncated"/> is recorded on the batch item.
    /// </summary>
    public static RenderedPrompt RenderUser(EnrichmentPromptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var (description, wasTruncated) = Truncate(input.Description ?? string.Empty, MaxDescriptionChars);
        var salary = string.IsNullOrWhiteSpace(input.PublishedSalary) ? "none" : input.PublishedSalary.Trim();

        var content = string.Create(CultureInfo.InvariantCulture, $"""
            Company: {input.CompanyName} ({input.CanonicalDomain})
            Title: {input.Title}
            Location: {input.LocationSummary}
            Published salary: {salary}
            Employment type: {input.EmploymentType}

            --- POSTING ---
            {description}
            --- END POSTING ---
            """);

        return new RenderedPrompt(content, wasTruncated);
    }

    /// <summary>
    /// Truncates at the last paragraph boundary (blank line) at or before <paramref name="max"/>, falling
    /// back to the last sentence-ending punctuation, then to a hard cut — never leaving a dangling
    /// half-sentence when a cleaner boundary is available.
    /// </summary>
    private static (string Text, bool WasTruncated) Truncate(string text, int max)
    {
        if (text.Length <= max)
        {
            return (text, false);
        }

        var window = text[..max];

        var paragraph = window.LastIndexOf("\n\n", StringComparison.Ordinal);
        if (paragraph > 0)
        {
            return (window[..paragraph].TrimEnd(), true);
        }

        var sentence = window.LastIndexOfAny(['.', '!', '?']);
        if (sentence > 0)
        {
            return (window[..(sentence + 1)].TrimEnd(), true);
        }

        return (window.TrimEnd(), true);
    }
}

/// <summary>A rendered user prompt and whether the posting text was truncated to fit.</summary>
public sealed record RenderedPrompt(string Content, bool WasTruncated);
