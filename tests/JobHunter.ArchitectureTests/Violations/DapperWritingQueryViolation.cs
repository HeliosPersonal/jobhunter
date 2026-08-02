namespace JobHunter.ArchitectureTests.Violations;

/// <summary>
/// Rule 4 broken: a read-model "Query" type that calls a Dapper write method. The production rule
/// scans the <c>src/</c> tree, so this fixture never trips it; <see cref="ViolationFixturesTests"/>
/// points the same scan at the Violations folder and proves it goes red on this file.
/// </summary>
public sealed class DapperWritingQueryViolation
{
    // The token the scan looks for: a write method invoked from a *Query* file. The violation is the
    // source text, not runtime behaviour — this class is never referenced by production code.
    private readonly Fake _connection = new();

    public int Corrupt() => _connection.ExecuteAsync("delete from jobs");

    private sealed class Fake
    {
        private int _calls;

        public int ExecuteAsync(string sql) => _calls += sql.Length;
    }
}
