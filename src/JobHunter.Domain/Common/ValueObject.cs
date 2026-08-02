namespace JobHunter.Domain.Common;

/// <summary>
/// Base type for value objects: equality is by the sequence of components, never by reference.
/// Value objects validate themselves in a <c>TryCreate</c>/<c>Create</c> pair (coding-standards §5).
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    /// <summary>The components that define equality, in a stable order.</summary>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other)
    {
        if (other is null || other.GetType() != GetType())
        {
            return false;
        }

        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override bool Equals(object? obj) => obj is ValueObject other && Equals(other);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var component in GetEqualityComponents())
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(ValueObject? left, ValueObject? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);
}
