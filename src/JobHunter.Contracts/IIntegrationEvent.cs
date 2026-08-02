namespace JobHunter.Contracts;

/// <summary>
/// Marker for every versioned integration event. Events are <c>PascalCase</c>, past tense, and carry
/// <see cref="OccurredAt"/> (event-catalog rule 4). Concrete events arrive with their features.
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>The UTC instant the event's cause occurred.</summary>
    DateTimeOffset OccurredAt { get; }
}
