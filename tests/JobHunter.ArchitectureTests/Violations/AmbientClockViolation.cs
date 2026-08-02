namespace JobHunter.ArchitectureTests.Violations;

/// <summary>
/// Rule 5 broken: a type reading the ambient clock directly instead of through <c>IClock</c>. The
/// production rule scans the <c>src/</c> tree and excludes <c>SystemClock.cs</c>; here the same scan is
/// pointed at the Violations folder to prove it goes red on this file.
/// </summary>
public sealed class AmbientClockViolation
{
    public static DateTime Now => DateTime.UtcNow;
}
