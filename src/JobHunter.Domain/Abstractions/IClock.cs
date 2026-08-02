namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The single source of time in the system. Nothing time-dependent reads the ambient clock directly;
/// architecture rule 5 bans <c>DateTime.Now</c>/<c>UtcNow</c> outside <see cref="SystemClock"/>.
/// </summary>
public interface IClock
{
    /// <summary>The current instant in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
