namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The single source of identity. Ids are UUID v7 (time-ordered) so primary keys stay index-friendly
/// and are safe to expose. Tests inject a deterministic generator instead
/// (<c>SequentialIdGenerator</c> in the TestKit).
/// </summary>
public interface IIdGenerator
{
    /// <summary>Produces a new, monotonically-increasing-within-a-process identifier.</summary>
    Guid NewId();
}
