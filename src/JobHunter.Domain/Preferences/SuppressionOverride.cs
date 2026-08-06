using JobHunter.Domain.Common;

namespace JobHunter.Domain.Preferences;

/// <summary>
/// An Owner instruction that outranks learning entirely for one <c>(dimension, value)</c> (F7
/// [[data-model]] §suppression_overrides). Unlike a <see cref="PreferenceWeight"/>, which is inferred and
/// carries evidence, an override is stated: the Owner declares that Berlin roles must always appear
/// (<see cref="SuppressionMode.NeverSuppress"/>) or must always be hidden
/// (<see cref="SuppressionMode.AlwaysSuppress"/>), and the model does not get a vote. One rule per value is
/// a database constraint (<c>uq_suppression_overrides</c>).
/// </summary>
public sealed class SuppressionOverride : Entity
{
    public SuppressionOverride(
        Guid id,
        Dimension dimension,
        string value,
        SuppressionMode mode,
        DateTimeOffset createdAt)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        Dimension = dimension;
        Value = value.Trim();
        Mode = mode;
        CreatedAt = createdAt;
    }

    private SuppressionOverride()
    {
    }

    public Dimension Dimension { get; private set; }

    public string Value { get; private set; } = null!;

    public SuppressionMode Mode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
