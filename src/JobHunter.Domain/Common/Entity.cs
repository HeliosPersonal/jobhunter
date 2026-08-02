namespace JobHunter.Domain.Common;

/// <summary>
/// Base type for aggregate roots and entities: identity is the <see cref="Id"/>, assigned once from
/// <c>IIdGenerator</c> (never database-generated — see the data-model <c>ValueGeneratedNever()</c> rule).
/// </summary>
public abstract class Entity
{
    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Entity id must not be empty.", nameof(id));
        }

        Id = id;
    }

    /// <summary>EF Core materialisation constructor.</summary>
    protected Entity()
    {
    }

    public Guid Id { get; protected init; }

    public override bool Equals(object? obj) =>
        obj is Entity other && other.GetType() == GetType() && other.Id == Id;

    public override int GetHashCode() => Id.GetHashCode();
}
