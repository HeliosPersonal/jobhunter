using System.Globalization;
using JobHunter.Domain.Common;

namespace JobHunter.Domain.Jobs;

/// <summary>
/// A published pay range, carrying its currency and period so a figure can never be silently averaged
/// across either (T01). The type is the safeguard: comparing two ranges in different currencies or over
/// different periods <em>throws</em>, because a number produced from euros-versus-dollars would be a
/// lie the digest must never tell. Amounts are <c>decimal</c>, never <c>double</c> (coding-standards
/// §5). A range is only ever built from figures a provider actually published — nothing here is
/// inferred.
/// </summary>
public sealed class SalaryRange : ValueObject
{
    public static readonly Error NoAmount =
        new("job.salary.no_amount", "A salary range needs at least one of min or max.");

    public static readonly Error BlankCurrency =
        new("job.salary.blank_currency", "A salary range needs a non-blank ISO-4217 currency.");

    public static readonly Error BadCurrency =
        new("job.salary.bad_currency", "A salary currency must be a three-letter ISO-4217 code.");

    public static readonly Error NegativeAmount =
        new("job.salary.negative", "A salary amount cannot be negative.");

    private SalaryRange(decimal min, decimal max, string currency, SalaryPeriod period, bool minMaxSwapped)
    {
        Min = min;
        Max = max;
        Currency = currency;
        Period = period;
        MinMaxSwapped = minMaxSwapped;
    }

    /// <summary>
    /// EF Core materialisation constructor. A stored range is always normalised (min ≤ max), so the
    /// swap flag — a parse-time anomaly, not a persisted column — is false on rehydration.
    /// </summary>
    private SalaryRange(decimal min, decimal max, string currency, SalaryPeriod period)
        : this(min, max, currency, period, false)
    {
    }

    /// <summary>The lower bound (equals <see cref="Max"/> when only one figure was published).</summary>
    public decimal Min { get; }

    /// <summary>The upper bound (equals <see cref="Min"/> when only one figure was published).</summary>
    public decimal Max { get; }

    /// <summary>The upper-cased ISO-4217 currency code, e.g. <c>USD</c>.</summary>
    public string Currency { get; }

    public SalaryPeriod Period { get; }

    /// <summary>
    /// True when the source published min above max and this range swapped them — an anomaly worth
    /// recording rather than trusting the source's ordering (T01).
    /// </summary>
    public bool MinMaxSwapped { get; }

    /// <summary>
    /// Builds a range from published figures. At least one of <paramref name="min"/>/<paramref name="max"/>
    /// must be present; a single figure becomes a point range. If both are present and min &gt; max they
    /// are swapped and <see cref="MinMaxSwapped"/> is set. Returns a failure (never throws) for a blank or
    /// malformed currency, a negative amount, or no amount at all.
    /// </summary>
    public static Result<SalaryRange> TryCreate(
        decimal? min,
        decimal? max,
        string? currency,
        SalaryPeriod period)
    {
        if (min is null && max is null)
        {
            return NoAmount;
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            return BlankCurrency;
        }

        var code = currency.Trim().ToUpperInvariant();
        if (!IsIso4217Shape(code))
        {
            return BadCurrency;
        }

        // A single published figure is a point range: the other bound mirrors it.
        var low = min ?? max!.Value;
        var high = max ?? min!.Value;

        if (low < 0 || high < 0)
        {
            return NegativeAmount;
        }

        var swapped = false;
        if (low > high)
        {
            (low, high) = (high, low);
            swapped = true;
        }

        return Result<SalaryRange>.Success(new SalaryRange(low, high, code, period, swapped));
    }

    /// <summary>
    /// True when this range is entirely above <paramref name="other"/> (its min exceeds the other's max).
    /// <strong>Throws</strong> <see cref="InvalidOperationException"/> when the currencies or periods
    /// differ — comparing across either is a programmer error, not a business outcome, because the answer
    /// would be meaningless (T01).
    /// </summary>
    public bool IsHigherThan(SalaryRange other)
    {
        EnsureComparable(other);
        return Min > other.Max;
    }

    /// <summary>
    /// Orders two comparable ranges by their midpoints. <strong>Throws</strong> when the currencies or
    /// periods differ, for the same reason as <see cref="IsHigherThan"/>.
    /// </summary>
    public int CompareTo(SalaryRange other)
    {
        EnsureComparable(other);
        var mine = (Min + Max) / 2m;
        var theirs = (other.Min + other.Max) / 2m;
        return mine.CompareTo(theirs);
    }

    public override string ToString() =>
        Min == Max
            ? string.Create(CultureInfo.InvariantCulture, $"{Currency} {Min}/{Period}")
            : string.Create(CultureInfo.InvariantCulture, $"{Currency} {Min}-{Max}/{Period}");

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Min;
        yield return Max;
        yield return Currency;
        yield return Period;
    }

    private void EnsureComparable(SalaryRange other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot compare salaries across currencies ({Currency} vs {other.Currency}).");
        }

        if (Period != other.Period)
        {
            throw new InvalidOperationException(
                $"Cannot compare salaries across periods ({Period} vs {other.Period}).");
        }
    }

    private static bool IsIso4217Shape(string code)
    {
        if (code.Length != 3)
        {
            return false;
        }

        foreach (var c in code)
        {
            if (c is < 'A' or > 'Z')
            {
                return false;
            }
        }

        return true;
    }
}
