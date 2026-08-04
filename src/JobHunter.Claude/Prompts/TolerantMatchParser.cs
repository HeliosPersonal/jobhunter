using System.Text.Json;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;

namespace JobHunter.Claude.Prompts;

/// <summary>
/// Turns one match batch-result item's raw tool-use JSON into a <see cref="MatchOutput"/>, applying the
/// match-schema parsing rules in order. Each step either passes, repairs, or records a failure and stops —
/// <strong>it never throws for a single item</strong> (QG-3). The step that matters at 03:00: an
/// unrecognised interview-probability band degrades to <see cref="InterviewProbability.Low"/> (the
/// pessimistic direction — the candidate would rather be surprised upward) and is noted as an anomaly
/// rather than crashing the poller (AC per T04 done-when).
///
/// <para>This parser sees only the model's output; no CV text is present in <paramref name="rawJson"/>, so
/// the leakage suite (QG-2) never has to reason about this path carrying the CV.</para>
/// </summary>
public static class TolerantMatchParser
{
    /// <summary>
    /// Parses <paramref name="rawJson"/> (the tool-use input object). A <c>null</c>/blank input is itself a
    /// parse failure. The caller has already handled a provider error on the item before reaching here.
    /// </summary>
    public static MatchParseResult Parse(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return MatchParseResult.Failure("empty result payload");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(rawJson);
        }
        catch (JsonException ex)
        {
            return MatchParseResult.Failure($"malformed JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return MatchParseResult.Failure("result payload is not a JSON object");
            }

            var anomalies = new List<string>();

            // matchScore: required integer; out-of-range is clamped into [0,100] rather than rejected —
            // the model's arithmetic is not trusted, but a fit judgement it made is worth keeping.
            if (!TryReadInt(root, "matchScore", out var rawScore))
            {
                return MatchParseResult.Failure("missing or non-integer 'matchScore'");
            }

            var matchScore = Math.Clamp(rawScore, Match.MinScore, Match.MaxScore);
            if (matchScore != rawScore)
            {
                anomalies.Add($"matchScore {rawScore} clamped to {matchScore}");
            }

            // interviewProbability: an unrecognised or absent band degrades to Low (pessimistic) and is noted.
            var interviewProbability = ReadInterviewProbability(root, anomalies);

            // missingSkills: unknown names retained verbatim, capped at 10; an absent array is a legal empty.
            var missingSkills = ReadStringArray(root, "missingSkills", cap: Match.MaxMissingSkills);

            // reasons non-empty after trimming blanks. minItems:1 in the schema is the first line of
            // defence; this is the belt-and-braces check invariant 4 requires regardless (AC-02).
            var reasons = ReadStringArray(root, "reasons", cap: 5);
            if (reasons.Count == 0)
            {
                return MatchParseResult.Failure("empty 'reasons' — violates invariant 4");
            }

            // salaryExpectation present ⇒ validate, swap, or drop; null is a legal "cannot tell".
            var salaryResult = ReadSalaryExpectation(root, anomalies);
            if (salaryResult.IsFailure)
            {
                return MatchParseResult.Failure(salaryResult.Error!);
            }

            var output = new MatchOutput(matchScore, interviewProbability, missingSkills, salaryResult.Salary, reasons);
            return MatchParseResult.Success(output, anomalies);
        }
    }

    private static InterviewProbability ReadInterviewProbability(JsonElement root, List<string> anomalies)
    {
        const InterviewProbability fallback = InterviewProbability.Low;

        if (!root.TryGetProperty("interviewProbability", out var el) || el.ValueKind != JsonValueKind.String)
        {
            anomalies.Add($"'interviewProbability' absent or non-string → {fallback}");
            return fallback;
        }

        var raw = el.GetString();
        if (Enum.TryParse<InterviewProbability>(raw, ignoreCase: false, out var parsed)
            && Enum.IsDefined(parsed)
            && parsed != InterviewProbability.Unknown)
        {
            return parsed;
        }

        // An unrecognised band degrades to Low and is logged, never thrown (T04 done-when).
        anomalies.Add($"unrecognised 'interviewProbability' value '{raw}' → {fallback}");
        return fallback;
    }

    private static List<string> ReadStringArray(JsonElement root, string name, int cap)
    {
        var result = new List<string>();
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = item.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            result.Add(value.Trim());
            if (result.Count >= cap)
            {
                break;
            }
        }

        return result;
    }

    private static SalaryReadResult ReadSalaryExpectation(JsonElement root, List<string> anomalies)
    {
        if (!root.TryGetProperty("salaryExpectation", out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return SalaryReadResult.None();
        }

        if (el.ValueKind != JsonValueKind.Object)
        {
            return SalaryReadResult.Fail("'salaryExpectation' is neither an object nor null");
        }

        if (!TryReadDecimal(el, "min", out var min) || !TryReadDecimal(el, "max", out var max))
        {
            return SalaryReadResult.Fail("salaryExpectation missing numeric 'min'/'max'");
        }

        if (!el.TryGetProperty("currency", out var curEl) || curEl.ValueKind != JsonValueKind.String)
        {
            return SalaryReadResult.Fail("salaryExpectation missing 'currency'");
        }

        if (min < 0 || max < 0)
        {
            return SalaryReadResult.Fail("salaryExpectation has a negative amount");
        }

        // An inverted range is swapped rather than rejected (the model's ordering is not trusted); the
        // value object swaps too, but noting it here keeps the anomaly attributable to the wire.
        if (min > max)
        {
            (min, max) = (max, min);
            anomalies.Add("salaryExpectation range inverted → swapped");
        }

        var currency = curEl.GetString()!.Trim().ToUpperInvariant();
        var period = ReadPeriodOrDefault(el, anomalies);
        return SalaryReadResult.Ok(new SalaryExpectationDto(min, max, currency, period));
    }

    private static SalaryPeriod ReadPeriodOrDefault(JsonElement el, List<string> anomalies)
    {
        if (el.TryGetProperty("period", out var pEl) && pEl.ValueKind == JsonValueKind.String
            && Enum.TryParse<SalaryPeriod>(pEl.GetString(), ignoreCase: false, out var period)
            && Enum.IsDefined(period))
        {
            return period;
        }

        anomalies.Add($"salaryExpectation 'period' absent or unrecognised → {SalaryPeriod.Year}");
        return SalaryPeriod.Year;
    }

    private static bool TryReadInt(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var el)
            && el.ValueKind == JsonValueKind.Number
            && el.TryGetInt32(out value);
    }

    private static bool TryReadDecimal(JsonElement el, string name, out decimal value)
    {
        value = 0m;
        return el.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetDecimal(out value);
    }

    private sealed record SalaryReadResult(SalaryExpectationDto? Salary, string? Error)
    {
        public bool IsFailure => Error is not null;

        public static SalaryReadResult Ok(SalaryExpectationDto salary) => new(salary, null);

        public static SalaryReadResult None() => new(null, null);

        public static SalaryReadResult Fail(string reason) => new(null, reason);
    }
}
