using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Tests.Support;

/// <summary>
/// A minimal <see cref="ILogger{T}"/> that records each entry's level and rendered message, so a test can
/// assert what was — and, for the note handler, what was <em>not</em> — written to a log line (invariant 12).
/// Zero-network, zero-dependency: the whole logging seam these unit tests need.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
