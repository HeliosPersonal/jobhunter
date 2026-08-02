namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The one and only place allowed to read the ambient clock (architecture rule 5).
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
