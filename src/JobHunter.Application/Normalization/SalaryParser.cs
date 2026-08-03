using System.Globalization;
using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Normalization;

/// <summary>
/// Parses a free-text salary into a structured <see cref="SalaryRange"/> or, failing that, retains the
/// original text (T03, SAD §8). It handles ranges and single figures, <c>k</c>/<c>m</c> suffixes,
/// symbol and code currencies, and an explicit period ("per year", "/hr"). It is conservative by
/// design: an unrecognised currency or a non-numeric input ("Competitive") yields
/// <see cref="ParsedSalary.Raw"/> only — never a zero, never a null-coerced range. An inverted range is
/// swapped by <see cref="SalaryRange"/> itself and the anomaly surfaces on <see cref="SalaryRange.MinMaxSwapped"/>.
/// No clock, no randomness, invariant parsing only.
/// </summary>
public static class SalaryParser
{
    private static readonly (string Token, string Code)[] CurrencyTokens =
    [
        ("$", "USD"),
        ("usd", "USD"),
        ("€", "EUR"),
        ("eur", "EUR"),
        ("£", "GBP"),
        ("gbp", "GBP"),
    ];

    private static readonly (string Token, SalaryPeriod Period)[] PeriodTokens =
    [
        ("year", SalaryPeriod.Year),
        ("annum", SalaryPeriod.Year),
        ("annual", SalaryPeriod.Year),
        ("yr", SalaryPeriod.Year),
        ("pa", SalaryPeriod.Year),
        ("month", SalaryPeriod.Month),
        ("mo", SalaryPeriod.Month),
        ("day", SalaryPeriod.Day),
        ("daily", SalaryPeriod.Day),
        ("hour", SalaryPeriod.Hour),
        ("hourly", SalaryPeriod.Hour),
        ("hr", SalaryPeriod.Hour),
    ];

    /// <summary>
    /// Parses <paramref name="text"/>. A null/blank input is <see cref="ParsedSalary.None"/>; any other
    /// unparseable input keeps the raw text. <paramref name="defaultPeriod"/> is used when the text names
    /// no period (annual is the ATS default).
    /// </summary>
    public static ParsedSalary Parse(string? text, SalaryPeriod defaultPeriod = SalaryPeriod.Year)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ParsedSalary.None;
        }

        var raw = text.Trim();
        var lower = raw.ToLowerInvariant();

        var currency = DetectCurrency(lower);
        if (currency is null)
        {
            return new ParsedSalary(null, raw);
        }

        var amounts = ExtractAmounts(lower);
        if (amounts.Count == 0)
        {
            return new ParsedSalary(null, raw);
        }

        var period = DetectPeriod(lower) ?? defaultPeriod;

        var min = amounts[0];
        decimal? max = amounts.Count > 1 ? amounts[1] : null;

        var range = SalaryRange.TryCreate(min, max, currency, period);
        return range.IsSuccess
            ? new ParsedSalary(range.Value, raw)
            : new ParsedSalary(null, raw);
    }

    private static string? DetectCurrency(string lower)
    {
        foreach (var (token, code) in CurrencyTokens)
        {
            if (lower.Contains(token, StringComparison.Ordinal))
            {
                return code;
            }
        }

        return null;
    }

    private static SalaryPeriod? DetectPeriod(string lower)
    {
        foreach (var (token, period) in PeriodTokens)
        {
            if (ContainsWord(lower, token))
            {
                return period;
            }
        }

        return null;
    }

    private static List<decimal> ExtractAmounts(string lower)
    {
        var amounts = new List<decimal>();
        var i = 0;

        while (i < lower.Length && amounts.Count < 2)
        {
            if (!char.IsDigit(lower[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < lower.Length && (char.IsDigit(lower[i]) || lower[i] is ',' or '.'))
            {
                i++;
            }

            var digits = lower[start..i].Replace(",", string.Empty, StringComparison.Ordinal);

            var multiplier = 1m;
            if (i < lower.Length && lower[i] == 'k')
            {
                multiplier = 1_000m;
                i++;
            }
            else if (i < lower.Length && lower[i] == 'm')
            {
                multiplier = 1_000_000m;
                i++;
            }

            if (decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                amounts.Add(value * multiplier);
            }
        }

        return amounts;
    }

    private static bool ContainsWord(string text, string word)
    {
        var index = text.IndexOf(word, StringComparison.Ordinal);
        while (index >= 0)
        {
            var before = index == 0 || !char.IsLetter(text[index - 1]);
            var afterIndex = index + word.Length;
            var after = afterIndex >= text.Length || !char.IsLetter(text[afterIndex]);
            if (before && after)
            {
                return true;
            }

            index = text.IndexOf(word, index + 1, StringComparison.Ordinal);
        }

        return false;
    }
}
