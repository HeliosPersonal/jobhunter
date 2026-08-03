using System.Globalization;
using JobHunter.Domain.Common;

namespace JobHunter.Domain.Intelligence;

/// <summary>
/// What the Owner could plausibly ask for <em>this</em> role (data-model §matches
/// <c>salary_expectation_*</c>) — the fit-side counterpart to a job's as-published
/// <see cref="JobHunter.Domain.Jobs.SalaryRange"/> and the enrichment's
/// <see cref="SalaryEstimate"/>. It carries only an amount range and a currency, which is exactly what
/// the three <c>salary_expectation_min/_max/_currency</c> columns store; unlike an enrichment estimate it
/// has no separate confidence, because the Match's own <c>match_score</c> already expresses uncertainty.
/// Amounts are <c>decimal</c>, never <c>double</c> (coding-standards §5).
/// </summary>
public sealed class SalaryExpectation : ValueObject
{
    public static readonly Error NegativeAmount =
        new("match.salary_expectation.negative", "A salary expectation amount cannot be negative.");

    public static readonly Error BadCurrency =
        new("match.salary_expectation.bad_currency", "A salary expectation currency must be a three-letter ISO-4217 code.");

    private SalaryExpectation(decimal min, decimal max, string currency)
    {
        Min = min;
        Max = max;
        Currency = currency;
    }

    public decimal Min { get; }

    public decimal Max { get; }

    /// <summary>The upper-cased ISO-4217 currency code, e.g. <c>EUR</c>.</summary>
    public string Currency { get; }

    /// <summary>
    /// Builds an expectation. Inverted bounds are swapped (the model's ordering is not trusted). Returns a
    /// failure — never throws — for a malformed currency or a negative amount, so the parser can drop the
    /// salary and keep the rest of the match.
    /// </summary>
    public static Result<SalaryExpectation> TryCreate(decimal min, decimal max, string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return BadCurrency;
        }

        var code = currency.Trim().ToUpperInvariant();
        if (code.Length != 3 || code.Any(c => c is < 'A' or > 'Z'))
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

        return Result<SalaryExpectation>.Success(new SalaryExpectation(min, max, code));
    }

    public override string ToString() =>
        Min == Max
            ? string.Create(CultureInfo.InvariantCulture, $"{Currency} {Min}")
            : string.Create(CultureInfo.InvariantCulture, $"{Currency} {Min}-{Max}");

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Min;
        yield return Max;
        yield return Currency;
    }
}
