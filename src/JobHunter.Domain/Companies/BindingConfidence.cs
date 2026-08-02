using JobHunter.Domain.Common;

namespace JobHunter.Domain.Companies;

/// <summary>
/// How strongly the evidence supports a binding, in [0, 1] (contract §Detection probes). A binding
/// needs <see cref="DiscoveryThreshold"/> (0.80) to be used for discovery — attributing another
/// company's jobs is a far worse failure than missing a company, so the bar is deliberately high.
/// Persisted as <c>numeric(3,2)</c>.
/// </summary>
public sealed class BindingConfidence : ValueObject
{
    /// <summary>The minimum confidence a binding needs before a company may be activated for discovery.</summary>
    public const decimal DiscoveryThreshold = 0.80m;

    public static readonly Error OutOfRange =
        new("binding.confidence.out_of_range", "Confidence must be between 0 and 1 inclusive.");

    private BindingConfidence(decimal value) => Value = value;

    public decimal Value { get; }

    /// <summary>True when the confidence meets the discovery threshold (≥ 0.80).</summary>
    public bool IsConfident => Value >= DiscoveryThreshold;

    public static Result<BindingConfidence> TryCreate(decimal value)
    {
        if (value < 0m || value > 1m)
        {
            return OutOfRange;
        }

        // Store to two decimal places to match numeric(3,2); scoring never needs finer resolution.
        return Result<BindingConfidence>.Success(new BindingConfidence(Math.Round(value, 2, MidpointRounding.AwayFromZero)));
    }

    public override string ToString() => Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
