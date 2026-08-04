using System.Globalization;
using JobHunter.Domain.Reporting;

namespace JobHunter.Claude.Prompts;

/// <summary>
/// The versioned digest-narrative prompt (F5 SAD §6.1, T05). The system prompt is a constant; the user
/// content is a pure function of <see cref="NarrativeInput"/>, so a change to either is visible in a
/// snapshot diff and bumps <see cref="PromptVersion"/> (which is stamped on the batch and on the synthesised
/// digest, so "the digest read oddly on Tuesday" is attributable to a specific prompt — SAD S4).
///
/// <para>The user content is built <strong>only from the day's aggregate counts and one salary
/// statistic</strong> — the numbers already destined for the digest header and footer. No CV text, no card
/// reason and no job description enters here: the CV crosses exactly one boundary (F4's match prompt) and it
/// is not this one. A market note is prose about the market, not about the Owner.</para>
/// </summary>
public static class DigestNarrativePrompt
{
    /// <summary>Bump whenever the system prompt, the user template or the output schema changes.</summary>
    public const string PromptVersion = "digest-narrative-v1";

    public const string System =
        """
        You write a single short market note for a daily digest of software engineering jobs, addressed to
        one engineer triaging their morning. You are given only aggregate counts for the day and, when there
        is one, an average advertised salary. You are given NOTHING about the reader — no CV, no skills, no
        preferences — so you never address their fit; you describe the day's market, not the person.

        Rules:
        - Two or three sentences, plain and calm. No greeting, no sign-off, no emoji, no markdown.
        - Ground every clause in a number you were given. Do not invent counts, trends, companies or roles.
        - You were given today's numbers only, not history, so never claim a comparison to "yesterday",
          "last week" or "usual" — there is no baseline to compare against.
        - When the day is quiet (few or no new roles, nothing strong), say so plainly rather than inflating it.
        - Mention the average salary only if one was provided; if it was not, do not speculate about pay.
        - Note suppressed, carried-over or degraded-source counts only when they are non-zero and material.
        - This is a market note, not advice: never tell the reader to apply, and never rank the roles.
        """;

    /// <summary>
    /// Renders the user content. Pure — no clock, no state — so the rendering is snapshot-tested. The
    /// average salary is rendered as a whole-dollar figure or the literal <c>none</c>, and every count is
    /// stated so the model has no room to invent one.
    /// </summary>
    public static string Render(NarrativeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var avgSalary = input.AvgSalaryUsd is { } salary
            ? string.Create(CultureInfo.InvariantCulture, $"${salary:0} USD")
            : "none";

        return string.Create(CultureInfo.InvariantCulture, $"""
            Today's digest facts:
            - New roles discovered: {input.TotalNewJobs}
            - Strong matches (shown): {input.StrongMatches}
            - Cards presented: {input.CardCount}
            - Average advertised salary: {avgSalary}
            - Scores suppressed with a reason: {input.SuppressedCount}
            - Items carried over from a missed batch: {input.CarriedOverCount}
            - Sources degraded or quarantined: {input.DegradedSourceCount}

            Write the market note.
            """);
    }
}
