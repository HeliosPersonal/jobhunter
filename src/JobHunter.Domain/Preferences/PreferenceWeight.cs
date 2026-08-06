using System.Collections.ObjectModel;
using JobHunter.Domain.Common;

namespace JobHunter.Domain.Preferences;

/// <summary>
/// A learned preference for one <c>(dimension, value)</c> — "the Owner reacts negatively to Berlin roles"
/// — carrying the evidence that produced it (F7 [[data-model]] §preference_weights,
/// [[adr/0002-evidence-threshold-and-explainability|ADR-F7-0002]]). The <see cref="Weight"/> is a signed
/// pull in <c>[-1, +1]</c> on the preference component of the F4 score.
///
/// <para>The construction guard <em>is</em> the ADR: a weight cannot be built with fewer than
/// <see cref="MinSupportingSignals"/> supporting signal ids, so the evidence floor (AC-03) is a type-level
/// property rather than a validation step a caller can skip — below three, a rate is a coincidence, not a
/// preference. The evidence is stored, not recomputed: <see cref="SupportingSignalIds"/> is the whole of
/// QG-1 (the actual ids), and <see cref="PositiveRate"/> is retained so the one-sentence explanation
/// ("34 of your last 38 ignores were below 170k EUR") stays stable after the evidence window moves on.</para>
///
/// <para>The Owner can disable a specific weight (AC-06); <see cref="Disable"/> records that it was
/// switched off and when, and it is never deleted — a disabled preference remains inspectable. The
/// aggregate is otherwise immutable.</para>
/// </summary>
public sealed class PreferenceWeight : Entity
{
    /// <summary>The evidence floor: below three supporting signals a rate is coincidence, not preference (AC-03).</summary>
    public const int MinSupportingSignals = 3;

    /// <summary>The lowest legal weight.</summary>
    public const decimal MinWeight = -1m;

    /// <summary>The highest legal weight.</summary>
    public const decimal MaxWeight = 1m;

    private readonly List<Guid> _supportingSignalIds = [];

    public PreferenceWeight(
        Guid id,
        Guid modelId,
        Dimension dimension,
        string value,
        decimal weight,
        IReadOnlyList<Guid> supportingSignalIds,
        decimal positiveRate,
        DateTimeOffset createdAt)
        : base(id)
    {
        if (modelId == Guid.Empty)
        {
            throw new ArgumentException("A PreferenceWeight must belong to a PreferenceModel.", nameof(modelId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentNullException.ThrowIfNull(supportingSignalIds);

        if (weight is < MinWeight or > MaxWeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(weight), weight, $"A preference weight must be in [{MinWeight}, {MaxWeight}].");
        }

        if (positiveRate is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(positiveRate), positiveRate, "A positive rate must be in [0, 1].");
        }

        var distinctIds = supportingSignalIds
            .Where(sid => sid != Guid.Empty)
            .Distinct()
            .ToList();

        if (distinctIds.Count < MinSupportingSignals)
        {
            // ADR-F7-0002 / AC-03 as a type-level property: a preference with fewer than three distinct
            // supporting signals is unrepresentable, so no weight can exist without inspectable evidence.
            throw new ArgumentException(
                $"A PreferenceWeight needs at least {MinSupportingSignals} distinct supporting signal ids "
                + $"(got {distinctIds.Count}).",
                nameof(supportingSignalIds));
        }

        _supportingSignalIds = distinctIds;
        ModelId = modelId;
        Dimension = dimension;
        Value = value.Trim();
        Weight = weight;
        PositiveRate = positiveRate;
        CreatedAt = createdAt;
    }

    private PreferenceWeight()
    {
    }

    public Guid ModelId { get; private set; }

    public Dimension Dimension { get; private set; }

    public string Value { get; private set; } = null!;

    /// <summary>The signed pull on the preference component, in <c>[-1, +1]</c>.</summary>
    public decimal Weight { get; private set; }

    /// <summary>The reaction rate that produced the weight, stored so the explanation can quote it stably.</summary>
    public decimal PositiveRate { get; private set; }

    /// <summary>True when the Owner switched this preference off (AC-06); it is retained, never deleted.</summary>
    public bool Disabled { get; private set; }

    /// <summary>When the Owner disabled it; null while active.</summary>
    public DateTimeOffset? DisabledAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The evidence, by id — non-empty and at least three by construction (QG-1).</summary>
    public IReadOnlyList<Guid> SupportingSignalIds => new ReadOnlyCollection<Guid>(_supportingSignalIds);

    /// <summary>How many signals support this weight — always <see cref="SupportingSignalIds"/>' count.</summary>
    public int SupportingSignalCount => _supportingSignalIds.Count;

    /// <summary>
    /// Records the Owner switching this preference off at <paramref name="disabledAt"/> (AC-06). Idempotent:
    /// disabling an already-disabled weight keeps the first timestamp, because the first switch-off is the
    /// one the explanation refers to.
    /// </summary>
    public void Disable(DateTimeOffset disabledAt)
    {
        if (Disabled)
        {
            return;
        }

        Disabled = true;
        DisabledAt = disabledAt;
    }
}
