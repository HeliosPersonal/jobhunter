using System.Globalization;
using System.Text;

namespace JobHunter.Claude.Prompts;

/// <summary>
/// The versioned company-research synthesis prompt (research-schema §Prompt). The system prompt is a
/// constant that states the one rule the whole feature rests on — every claim must come from a supplied
/// document and cite that document's exact URL, and a thin document set must yield a thin dossier. The user
/// content is a pure function of <see cref="ResearchPromptInput"/>, so a change to either is visible in a
/// snapshot diff and bumps <see cref="PromptVersion"/> (stamped on every batch, so a quality regression is
/// attributable to a specific change).
///
/// <para>Each document's text is capped at <see cref="MaxDocumentChars"/> so one large page cannot blow the
/// per-dossier token budget the cost estimate is built on. The categories that found no documents are listed
/// explicitly, because naming the known absence is the single most effective guard against the model filling
/// a gap from memory (research-schema §Prompt note).</para>
/// </summary>
public static class ResearchSynthesisPrompt
{
    /// <summary>Bump whenever the system prompt, the user template or the schema changes.</summary>
    public const string PromptVersion = "research-v1";

    /// <summary>Each document's extracted text is capped at this many characters in the prompt.</summary>
    public const int MaxDocumentChars = 20_000;

    /// <summary>
    /// The pessimistic per-item output ceiling the cost estimate prices against (research-schema §Cost). A
    /// dossier's output is the summary plus up to twenty short claims — about 800 tokens in practice; 900
    /// errs high, so the estimate over-states spend, which is the safe direction for the ceiling.
    /// </summary>
    public const int MaxOutputTokens = 900;

    public const string System =
        """
        You summarise what a set of documents says about a company. You are a summariser, not an expert.

        Absolute rules:
        - Every claim must be supported by one of the documents provided below. You may not use anything you
          know about this company from any other source. If the documents do not say it, it does not exist.
        - Every claim must cite the exact sourceUrl of the document that supports it, copied verbatim from
          the document headers. Do not construct, guess or normalise a URL.
        - If the documents are thin, produce few claims. A short honest dossier is correct; a rich one padded
          from memory is a failure.
        - Mark isWarning for layoffs, down rounds, funding difficulty, or credible reports of serious
          organisational problems.
        - One claim per sentence. State what the source says, not what you infer from it.
        - The summary must contain nothing that is not also in a claim.
        """;

    /// <summary>
    /// Renders the per-company user content. Pure — no clock, no state — so the rendering is snapshot-tested.
    /// </summary>
    public static string RenderUser(ResearchPromptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"Company: {input.DisplayName} ({input.CanonicalDomain})");
        builder.Append("\n\n--- DOCUMENTS ---");

        var index = 1;
        foreach (var entry in input.Documents)
        {
            var document = entry.Document;
            var observed = document.ObservedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var text = Cap(document.Text, MaxDocumentChars);

            builder.Append(CultureInfo.InvariantCulture, $"\n[{index}] sourceUrl: {document.Url}");
            builder.Append(CultureInfo.InvariantCulture, $"\n    category: {entry.Category}");
            builder.Append(CultureInfo.InvariantCulture, $"\n    observed: {observed}");
            builder.Append(CultureInfo.InvariantCulture, $"\n    title: {document.Title}");
            builder.Append(CultureInfo.InvariantCulture, $"\n    {text}");
            index++;
        }

        builder.Append("\n--- END DOCUMENTS ---\n\n");

        var empty = input.EmptyCategories.Count == 0
            ? "none"
            : string.Join(", ", input.EmptyCategories);
        builder.Append(CultureInfo.InvariantCulture, $"Categories with no documents found: {empty}");

        return builder.ToString();
    }

    private static string Cap(string text, int max) => text.Length <= max ? text : text[..max];
}
