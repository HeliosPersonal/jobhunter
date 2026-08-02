using JobHunter.Domain.Common;

namespace JobHunter.Infrastructure.Persistence.Reference;

/// <summary>
/// A framework-owned reference aggregate — not a domain table. It exists so the persistence idioms
/// (EF write repository + Dapper read query) have one worked example each for later features to copy,
/// and so the first migration creates a real table the harness can prove applies (T05/T07). It carries
/// a status enum to prove the enum-as-text convention and a UTC timestamp to prove <c>timestamptz</c>.
/// </summary>
public sealed class PlatformMarker : Entity
{
    public PlatformMarker(Guid id, string label, MarkerStatus status, DateTimeOffset recordedAt)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Label = label;
        Status = status;
        RecordedAt = recordedAt;
    }

    private PlatformMarker()
    {
        Label = string.Empty;
    }

    public string Label { get; private set; }

    public MarkerStatus Status { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public void Activate(DateTimeOffset at)
    {
        Status = MarkerStatus.Active;
        RecordedAt = at;
    }
}

/// <summary>Persisted as <c>text</c> (never an ordinal) to prove the convention.</summary>
public enum MarkerStatus
{
    Pending,
    Active,
    Retired,
}
