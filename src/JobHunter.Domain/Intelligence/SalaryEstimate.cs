using System.Globalization;
using JobHunter.Domain.Common;
using JobHunter.Domain.Jobs;

namespace JobHunter.Domain.Intelligence;

/// <summary>
/// The model's <em>estimated</em> pay range for a role — deliberately separate from the as-published
/// <see cref="SalaryRange"/> on the job (data-model §enrichments). It carries a
/// <see cref="Confidence"/> in <c>[0,1]</c> so ranking can discount a shaky estimate rather than trust
/// it equally, and — like <see cref="SalaryRange"/> — it refuses to compare across currencies or
/// periods, because a euros-versus-dollars answer would be a lie the digest must never tell. Amounts
/// are <c>decimal</c>, never <c>double</c> (coding-standards §5).
/// </summary>
public sealed class SalaryEstimate : ValueObject
{
    public static readonly Error NegativeAmount =
        new("enrichment.salary.negative", "An estimated salary amount cannot be negative.");

    public static readonly Error BadCurrency =
        new("enrichment.salary.bad_currency", "An estimated salary currency must be a three-letter ISO-4217 code.");

    private SalaryEstimate(decimal min, decimal max, string currency, SalaryPeriod period, decimal confidence)
    {
        Min = min;
        Max = max;
        Currency = currency;
        Period = period;
        Confidence = confidence;
    }

    public decimal Min { get; }

    public decimal Max { get; }

    /// <summary>The upper-cased ISO-4217 currency code, e.g. <c>USD</c>.</summary>
    public string Currency { get; }

    public SalaryPeriod Period { get; }

    /// <summary>Calibrated confidence in <c>[0,1]</c>; a low value lets ranking discount the estimate.</summary>
    public decimal Confidence { get; }

    /// <summary>
    /// Builds an estimate. Inverted bounds are swapped (the model's ordering is not trusted); a
    /// confidence outside <c>[0,1]</c> is clamped (parsing step 6). Returns a failure — never throws —
    /// for a malformed currency or a negative amount, so the caller can drop the salary and keep the rest
    /// of the enrichment (parsing step 5).
    /// </summary>
    public static Result<SalaryEstimate> TryCreate(
        decimal min,
        decimal max,
        string? currency,
        SalaryPeriod period,
        decimal confidence)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return BadCurrency;
        }

        var code = currency.Trim().ToUpperInvariant();
        if (!IsIso4217Shape(code))
        {
            return BadCurrency;
        }

        if (min < 0 || max < 0)
        {
            return NegativeAmount;
        }

        if (min > max)
        {
            (min, max) = (max, min);
        }

        var clamped = Math.Clamp(confidence, 0m, 1m);
        return Result<SalaryEstimate>.Success(new SalaryEstimate(min, max, code, period, clamped));
    }

    /// <summary>
    /// Orders two comparable estimates by midpoint. <strong>Throws</strong>
    /// <see cref="InvalidOperationException"/> when the currencies or periods differ — comparing across
    /// either is a programmer error, not a business outcome, because the answer would be meaningless.
    /// </summary>
    public int CompareTo(SalaryEstimate other)
    {
        EnsureComparable(other);
        var mine = (Min + Max) / 2m;
        var theirs = (other.Min + other.Max) / 2m;
        return mine.CompareTo(theirs);
    }

    public override string ToString() =>
        Min == Max
            ? string.Create(CultureInfo.InvariantCulture, $"~{Currency} {Min}/{Period} @{Confidence:0.00}")
            : string.Create(CultureInfo.InvariantCulture, $"~{Currency} {Min}-{Max}/{Period} @{Confidence:0.00}");

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Min;
        yield return Max;
        yield return Currency;
        yield return Period;
        yield return Confidence;
    }

    private void EnsureComparable(SalaryEstimate other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot compare estimated salaries across currencies ({Currency} vs {other.Currency}).");
        }

        if (Period != other.Period)
        {
            throw new InvalidOperationException(
                $"Cannot compare estimated salaries across periods ({Period} vs {other.Period}).");
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
