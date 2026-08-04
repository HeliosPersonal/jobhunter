using System.Globalization;
using System.Text;
using JobHunter.Domain.Reporting;

namespace JobHunter.Application.Reporting;

/// <summary>
/// The deterministic market note used whenever the model note is unavailable or over budget (F5 T05,
/// ADR-F5-0001). It is a pure function of the same <see cref="NarrativeInput"/> the model would have seen —
/// only aggregate counts and one salary statistic, nothing about the Owner — so the fallback carries no CV
/// content either. It always produces a non-blank sentence, including for a dead day, so the digest always
/// has a header line to render and the synthesiser never has to invent one.
/// </summary>
public static class NarrativeTemplate
{
    /// <summary>
    /// Renders a plain, calm note from the day's numbers. Deliberately unpretentious: it states what the day
    /// held and stops, which is exactly what the model is asked to do when it is available.
    /// </summary>
    public static string Render(NarrativeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!input.HasSomethingToSay)
        {
            return "A quiet day: no new roles cleared discovery, so there is nothing new to review.";
        }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{input.TotalNewJobs} new {Roles(input.TotalNewJobs)} today");

        if (input.StrongMatches > 0)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $", {input.StrongMatches} of them a strong match");
        }

        sb.Append('.');

        if (input.CardCount > 0 && input.AvgSalaryUsd is { } salary)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $" The {input.CardCount} shown {Cards(input.CardCount)} advertise around ${salary:0} USD on average.");
        }
        else if (input.CardCount > 0)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $" {input.CardCount} {Cards(input.CardCount)} shown.");
        }

        if (input.SuppressedCount > 0)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $" {input.SuppressedCount} lower-fit {Scores(input.SuppressedCount)} were suppressed.");
        }

        if (input.CarriedOverCount > 0)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $" {input.CarriedOverCount} {Items(input.CarriedOverCount)} carried over from a missed batch.");
        }

        if (input.DegradedSourceCount > 0)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $" {input.DegradedSourceCount} {Sources(input.DegradedSourceCount)} were degraded and may be under-covered.");
        }

        return sb.ToString();
    }

    private static string Roles(int n) => n == 1 ? "role" : "roles";

    private static string Cards(int n) => n == 1 ? "role" : "roles";

    private static string Scores(int n) => n == 1 ? "score" : "scores";

    private static string Items(int n) => n == 1 ? "item" : "items";

    private static string Sources(int n) => n == 1 ? "source" : "sources";
}
