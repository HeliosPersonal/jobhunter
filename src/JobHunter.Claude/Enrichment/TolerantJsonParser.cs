using System.Globalization;
using System.Text.Json;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;

namespace JobHunter.Claude.Enrichment;

/// <summary>
/// Turns one batch result item's raw tool-use JSON into an <see cref="EnrichmentOutput"/>, applying the
/// eight parsing steps in order (enrichment-schema §Parsing rules). Each step either passes, repairs, or
/// records a failure and stops — <strong>it never throws for a single item</strong>. Step 8 is the one
/// that matters at 03:00: an unrecognised enum value degrades to <c>Unknown</c> and is noted as an
/// anomaly rather than crashing the poller.
/// </summary>
public static class TolerantJsonParser
{
    /// <summary>
    /// Parses <paramref name="rawJson"/> (the tool-use input object). A <c>null</c>/blank input is itself a
    /// parse failure. The caller has already handled step 1 (a provider error on the item) before reaching
    /// here — this method covers steps 2 through 8.
    /// </summary>
    public static ParseOutcome Parse(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return ParseOutcome.Failure("empty result payload");
        }

        // Step 2: JSON parses.
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(rawJson);
        }
        catch (JsonException ex)
        {
            return ParseOutcome.Failure($"malformed JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ParseOutcome.Failure("result payload is not a JSON object");
            }

            var anomalies = new List<string>();

            // Step 3 (partial): required scalar fields present and the right shape.
            if (!TryReadBool(root, "isRemote", out var isRemote))
            {
                return ParseOutcome.Failure("missing or non-boolean 'isRemote'");
            }

            if (!TryReadBool(root, "isContractorFriendly", out var isContractorFriendly))
            {
                return ParseOutcome.Failure("missing or non-boolean 'isContractorFriendly'");
            }

            // Step 8: enums degrade to Unknown rather than throwing.
            var timezoneBand = ReadEnum(root, "timezoneBand", TimezoneBand.Unknown, anomalies);
            var aiUsage = ReadEnum(root, "aiUsage", AiUsageLevel.Unknown, anomalies);
            var companyStage = ReadEnum(root, "companyStage", CompanyStage.Unknown, anomalies);

            // Step 7: technologies — unknown names retained verbatim, capped at 25.
            var technologies = ReadStringArray(root, "technologies", cap: 25);

            // Step 4: reasons non-empty after trimming blanks. (minItems:1 in the schema is the first line
            // of defence; this is the belt-and-braces check invariant 4 requires regardless.)
            var reasons = ReadStringArray(root, "reasons", cap: 6);
            if (reasons.Count == 0)
            {
                return ParseOutcome.Failure("empty 'reasons' — violates invariant 4");
            }

            // Steps 5 and 6: salary present ⇒ validate, swap, clamp, or drop.
            var salaryResult = ReadSalary(root, anomalies);
            if (salaryResult.IsFailure)
            {
                return ParseOutcome.Failure(salaryResult.Error!);
            }

            var output = new EnrichmentOutput(
                salaryResult.Salary,
                isRemote,
                isContractorFriendly,
                timezoneBand,
                aiUsage,
                companyStage,
                technologies,
                reasons);

            return ParseOutcome.Success(output, anomalies);
        }
    }

    private static bool TryReadBool(JsonElement root, string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out var el))
        {
            return false;
        }

        if (el.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        return el.ValueKind == JsonValueKind.False;
    }

    private static TEnum ReadEnum<TEnum>(JsonElement root, string name, TEnum fallback, List<string> anomalies)
        where TEnum : struct, Enum
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String)
        {
            anomalies.Add($"'{name}' absent or non-string → {fallback}");
            return fallback;
        }

        var raw = el.GetString();
        if (Enum.TryParse<TEnum>(raw, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        // Step 8: an unrecognised value degrades and is logged, never thrown.
        anomalies.Add($"unrecognised '{name}' value '{raw}' → {fallback}");
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

    private static SalaryReadResult ReadSalary(JsonElement root, List<string> anomalies)
    {
        if (!root.TryGetProperty("salary", out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return SalaryReadResult.None();
        }

        if (el.ValueKind != JsonValueKind.Object)
        {
            return SalaryReadResult.Fail("'salary' is neither an object nor null");
        }

        if (!TryReadDecimal(el, "min", out var min) || !TryReadDecimal(el, "max", out var max))
        {
            return SalaryReadResult.Fail("salary missing numeric 'min'/'max'");
        }

        if (!el.TryGetProperty("currency", out var curEl) || curEl.ValueKind != JsonValueKind.String)
        {
            return SalaryReadResult.Fail("salary missing 'currency'");
        }

        if (min < 0 || max < 0)
        {
            return SalaryReadResult.Fail("salary has a negative amount");
        }

        // Step 5: swap an inverted range rather than rejecting it (the model's ordering is not trusted).
        if (min > max)
        {
            (min, max) = (max, min);
            anomalies.Add("salary range inverted → swapped");
        }

        // Step 5: an unknown currency drops the salary but keeps the rest of the assessment.
        var currency = curEl.GetString()!.Trim().ToUpperInvariant();
        if (!IsRealIso4217(currency))
        {
            anomalies.Add($"unknown currency '{currency}' → salary dropped");
            return SalaryReadResult.None();
        }

        var period = ReadEnumOrDefault(el, "period", SalaryPeriod.Year, anomalies);

        // Step 6: clamp confidence into [0,1] rather than rejecting it.
        var confidence = 0m;
        if (TryReadDecimal(el, "confidence", out var rawConfidence))
        {
            confidence = Math.Clamp(rawConfidence, 0m, 1m);
            if (confidence != rawConfidence)
            {
                anomalies.Add($"confidence {rawConfidence} clamped to {confidence}");
            }
        }

        return SalaryReadResult.Ok(new SalaryEstimateDto(min, max, currency, period, confidence));
    }

    private static SalaryPeriod ReadEnumOrDefault(
        JsonElement el, string name, SalaryPeriod fallback, List<string> anomalies)
    {
        if (el.TryGetProperty(name, out var pEl) && pEl.ValueKind == JsonValueKind.String
            && Enum.TryParse<SalaryPeriod>(pEl.GetString(), ignoreCase: false, out var period)
            && Enum.IsDefined(period))
        {
            return period;
        }

        anomalies.Add($"salary '{name}' absent or unrecognised → {fallback}");
        return fallback;
    }

    private static bool TryReadDecimal(JsonElement el, string name, out decimal value)
    {
        value = 0m;
        return el.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetDecimal(out value);
    }

    private static bool IsRealIso4217(string code)
    {
        if (code.Length != 3)
        {
            return false;
        }

        // A curated set is enough for the corpus; an unknown-but-well-shaped code still drops the salary,
        // which is the conservative outcome the contract asks for.
        return Iso4217.Contains(code);
    }

    private static readonly HashSet<string> Iso4217 = new(StringComparer.Ordinal)
    {
        "USD", "EUR", "GBP", "CHF", "CAD", "AUD", "NZD", "SEK", "NOK", "DKK", "PLN", "CZK", "HUF",
        "RON", "BGN", "UAH", "JPY", "CNY", "HKD", "SGD", "INR", "ILS", "AED", "SAR", "ZAR", "BRL",
        "MXN", "TRY", "KRW",
    };

    private sealed record SalaryReadResult(SalaryEstimateDto? Salary, string? Error)
    {
        public bool IsFailure => Error is not null;

        public static SalaryReadResult Ok(SalaryEstimateDto salary) => new(salary, null);

        public static SalaryReadResult None() => new(null, null);

        public static SalaryReadResult Fail(string reason) => new(null, reason);
    }
}
