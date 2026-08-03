using System.Globalization;
using JobHunter.Domain.Common;

namespace JobHunter.Domain.Pipeline;

/// <summary>
/// A priced token count — the output of the <see cref="Abstractions.ICostAccountant"/> both for the
/// estimate written <em>before</em> submission (QG-2) and for the actual written on retrieval
/// (data-model §cost_ledger_entries, ADR-F3-0002). All arithmetic is <c>decimal</c>, never
/// <c>double</c>: money that drifts by a rounding error over a Run's worth of entries would make the
/// ceiling meaningless (coding-standards §5).
/// </summary>
public sealed class CostEstimate : ValueObject
{
    public CostEstimate(decimal costUsd, int inputTokens, int outputTokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(costUsd);
        ArgumentOutOfRangeException.ThrowIfNegative(inputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(outputTokens);

        CostUsd = costUsd;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
    }

    /// <summary>The discounted cost in USD — the batch discount is already applied.</summary>
    public decimal CostUsd { get; }

    public int InputTokens { get; }

    public int OutputTokens { get; }

    /// <summary>The zero estimate — an empty batch costs nothing, which keeps the ceiling check total.</summary>
    public static CostEstimate Zero { get; } = new(0m, 0, 0);

    /// <summary>Sums two estimates. Used to total a Run's per-batch estimates against the ceiling.</summary>
    public CostEstimate Add(CostEstimate other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new CostEstimate(
            CostUsd + other.CostUsd,
            InputTokens + other.InputTokens,
            OutputTokens + other.OutputTokens);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"${CostUsd:0.0000} ({InputTokens} in / {OutputTokens} out)");

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CostUsd;
        yield return InputTokens;
        yield return OutputTokens;
    }
}
