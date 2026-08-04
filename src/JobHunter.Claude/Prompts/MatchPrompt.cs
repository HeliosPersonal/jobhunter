using System.Globalization;

namespace JobHunter.Claude.Prompts;

/// <summary>
/// The versioned match prompt (match-schema §Prompt). <strong>This is the only file in the whole codebase
/// that renders CV text into a string.</strong> It is built to make that structurally safe: it takes the
/// CV <em>by value</em> on <see cref="MatchPromptInput.CvText"/>, has <strong>no <c>ILogger</c> and no
/// <c>ActivitySource</c></strong> (asserted by an architecture test), and is a pure static function — so it
/// cannot emit the CV to a log or a span even by accident (match-schema §CV handling rules 1–2). A change
/// to the system prompt or either user template is visible in a snapshot diff and bumps
/// <see cref="PromptVersion"/>, which is stamped on every Match row (AC-11).
///
/// <para>The render is split into a stable <see cref="RenderedMatchPrompt.CvBlock"/> (system prompt + CV +
/// candidate preferences — byte-identical across every item in a matching batch) and a per-item
/// <see cref="RenderedMatchPrompt.RoleBlock"/>. Nothing volatile precedes the CV block's end: no timestamp,
/// no run id, no per-job value appears in the system prompt or the CV block, so a <c>cache_control</c>
/// breakpoint placed at its end stays valid across the batch (match-schema §Prompt caching, constraint 1;
/// enforced by T13's cache assertion).</para>
/// </summary>
public static class MatchPrompt
{
    /// <summary>Bump whenever the system prompt, either user template, the schema or a parsing rule changes.</summary>
    // match-v2 (T16): the candidate block gained the Owner's career-goal section (target families, desired
    // AI-usage floor, target titles), rendered only when a goal is stated.
    public const string PromptVersion = "match-v2";

    /// <summary>The CV text is truncated to this many characters at a section boundary.</summary>
    public const int MaxCvChars = 8_000;

    /// <summary>The posting text is truncated to this many characters at a paragraph boundary.</summary>
    public const int MaxDescriptionChars = 10_000;

    public const string System =
        """
        You assess how well a specific candidate fits a specific engineering role. You are blunt and
        calibrated. Your value is in saying no clearly.

        Rules:
        - Compare the candidate's demonstrated experience against what the role requires. Weight what they
          have actually done far above what they list as a skill.
        - matchScore is fit, not desirability. A perfect fit for a mediocre role scores high.
        - Missing skills means genuinely required and genuinely absent. Do not list nice-to-haves. An empty
          list is a valid and useful answer.
        - interviewProbability accounts for seniority gap, domain gap, location and visa constraints, and
          how competitive the role is. Be pessimistic: the candidate would rather be surprised upward.
        - salaryExpectation is what THIS candidate could plausibly ask for THIS role given their level and
          the market implied by the posting. Null if the posting gives you nothing to anchor on.
        - Every reason must be specific and reference something concrete from either the CV or the posting.
          "Good fit" is not a reason. "Seven years of Kafka against a role that names Kafka as core" is.
        - If the role is a poor fit, say so plainly and score it low. A generous score is a disservice.
        """;

    /// <summary>
    /// Renders the match prompt. Pure — no clock, no state, no logger — so the rendering is snapshot-tested.
    /// The CV and the posting are each truncated at a boundary and whether each was truncated is reported on
    /// <see cref="RenderedMatchPrompt"/> so the batch item can record it. When
    /// <see cref="MatchPromptInput.Enrichment"/> is <c>null</c> the enrichment-derived lines are omitted
    /// entirely rather than filled with <c>Unknown</c> (AC-09).
    /// </summary>
    public static RenderedMatchPrompt Render(MatchPromptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var (cvText, cvTruncated) = TruncateAtSection(input.CvText ?? string.Empty, MaxCvChars);
        var cvBlock = RenderCvBlock(input, cvText);

        var (description, descriptionTruncated) = TruncateAtParagraph(input.Description ?? string.Empty, MaxDescriptionChars);
        var roleBlock = RenderRoleBlock(input, description);

        return new RenderedMatchPrompt(cvBlock, roleBlock, cvTruncated, descriptionTruncated);
    }

    private static string RenderCvBlock(MatchPromptInput input, string cvText)
    {
        var salaryFloor = input.SalaryFloor is { } floor && !string.IsNullOrWhiteSpace(input.SalaryFloorCurrency)
            ? string.Create(CultureInfo.InvariantCulture, $"{floor} {input.SalaryFloorCurrency}")
            : "none";

        // T16: the Owner's career goal — a Profile fact, stable per Profile, so it lives in the cacheable
        // candidate block. Omitted entirely when no goal is stated, rather than filled with placeholders, so
        // the model is not invited to reason about a goal that does not exist (same principle as AC-09).
        var goalLines = RenderGoalLines(input);

        return string.Create(CultureInfo.InvariantCulture, $"""
            --- CANDIDATE ---
            {cvText}
            Salary floor: {salaryFloor}
            Timezone: {input.OwnerTimezoneBand}
            Open to: {input.EmploymentTypesOpenTo}{goalLines}
            --- END CANDIDATE ---
            """);
    }

    private static string RenderGoalLines(MatchPromptInput input)
    {
        var hasFamilies = !string.IsNullOrWhiteSpace(input.TargetRoleFamilies);
        var hasFloor = !string.IsNullOrWhiteSpace(input.DesiredAiUsageFloor);
        var hasTitles = !string.IsNullOrWhiteSpace(input.TargetTitles);

        if (!hasFamilies && !hasFloor && !hasTitles)
        {
            return string.Empty;
        }

        // The goal directive (match-schema §Prompt, TUNE-05): fit is not desirability, so the model is told
        // to reward a genuine stretch toward the trajectory and down-weight a repeat of the current track.
        var targeting = hasFamilies ? input.TargetRoleFamilies!.Trim() : "roles outside their current track";
        var directive = string.Create(CultureInfo.InvariantCulture, $"""

            Career goal: the candidate is deliberately targeting {targeting}. Reward genuine alignment to that
            trajectory even where it is a stretch; down-weight roles that would repeat their current track.
            """);

        var floorLine = hasFloor
            ? string.Create(CultureInfo.InvariantCulture, $"\nDesired AI-usage floor: {input.DesiredAiUsageFloor!.Trim()}")
            : string.Empty;
        var titleLine = hasTitles
            ? string.Create(CultureInfo.InvariantCulture, $"\nTarget titles: {input.TargetTitles!.Trim()}")
            : string.Empty;

        return directive + floorLine + titleLine;
    }

    private static string RenderRoleBlock(MatchPromptInput input, string description)
    {
        var seniority = string.IsNullOrWhiteSpace(input.Seniority) ? "unspecified" : input.Seniority.Trim();
        var publishedSalary = string.IsNullOrWhiteSpace(input.PublishedSalary) ? "none" : input.PublishedSalary.Trim();

        var header = string.Create(CultureInfo.InvariantCulture, $"""
            --- ROLE ---
            Company: {input.CompanyName}
            Title: {input.Title} · Seniority: {seniority}
            Location: {input.LocationSummary}
            Employment: {input.EmploymentType}
            Published salary: {publishedSalary}
            """);

        // AC-09: a missing enrichment omits these lines entirely — a prompt padded with "Unknown" invites
        // the model to reason about the unknowns; omitting the lines does not.
        var enrichmentLines = RenderEnrichmentLines(input.Enrichment);

        return string.Create(CultureInfo.InvariantCulture, $"""
            {header}{enrichmentLines}

            {description}
            --- END ROLE ---
            """);
    }

    private static string RenderEnrichmentLines(MatchEnrichmentFacts? e)
    {
        if (e is null)
        {
            return string.Empty;
        }

        var estimatedSalary = string.IsNullOrWhiteSpace(e.EstimatedSalary) ? "none" : e.EstimatedSalary.Trim();
        var confidence = e.SalaryConfidence is { } c
            ? string.Create(CultureInfo.InvariantCulture, $"{c}")
            : "unknown";

        return string.Create(CultureInfo.InvariantCulture, $"""

            Company stage: {e.CompanyStage}
            Remote: {e.IsRemote} · Timezone: {e.TimezoneBand} · Contractor friendly: {e.IsContractorFriendly}
            Estimated salary: {estimatedSalary} (confidence {confidence})
            Technologies: {e.Technologies}
            AI usage: {e.AiUsage}
            """);
    }

    /// <summary>
    /// Truncates the CV at the last section boundary (a blank line) at or before <paramref name="max"/>,
    /// falling back to a hard cut — never leaving a dangling half-section when a cleaner boundary exists.
    /// </summary>
    private static (string Text, bool WasTruncated) TruncateAtSection(string text, int max)
    {
        if (text.Length <= max)
        {
            return (text, false);
        }

        var window = text[..max];

        var section = window.LastIndexOf("\n\n", StringComparison.Ordinal);
        if (section > 0)
        {
            return (window[..section].TrimEnd(), true);
        }

        return (window.TrimEnd(), true);
    }

    /// <summary>
    /// Truncates the posting at the last paragraph boundary (a blank line) at or before
    /// <paramref name="max"/>, falling back to the last sentence-ending punctuation, then to a hard cut.
    /// </summary>
    private static (string Text, bool WasTruncated) TruncateAtParagraph(string text, int max)
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

/// <summary>
/// A rendered match prompt, split for prompt caching (match-schema §Prompt caching). The
/// <see cref="CvBlock"/> is the stable prefix — system prompt aside, byte-identical across every item in a
/// matching batch — and carries the CV; the <see cref="RoleBlock"/> is the per-item suffix. A
/// <c>cache_control</c> breakpoint is placed at the end of the CV block (T13). <see cref="CvWasTruncated"/>
/// and <see cref="DescriptionWasTruncated"/> are recorded on the batch item so a poor assessment can be
/// checked against them.
/// </summary>
public sealed record RenderedMatchPrompt(
    string CvBlock,
    string RoleBlock,
    bool CvWasTruncated,
    bool DescriptionWasTruncated)
{
    /// <summary>True when either the CV or the posting was truncated to fit — recorded on the batch item.</summary>
    public bool WasTruncated => CvWasTruncated || DescriptionWasTruncated;
}
