using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Common;

/// <summary>
/// Opens one pipeline span and one correlated logging scope for a unit of work, so no handler has to
/// remember to (T11 / AC-05). Disposing it closes both. The correlation id flows onto the span as an
/// attribute and into the log scope as <c>correlation_id</c>, giving one end-to-end trace and
/// correlated logs across stages.
/// </summary>
public sealed class CorrelationScope : IDisposable
{
    private readonly Activity? _activity;
    private readonly IDisposable? _logScope;
    private bool _disposed;

    private CorrelationScope(Activity? activity, IDisposable? logScope)
    {
        _activity = activity;
        _logScope = logScope;
    }

    /// <summary>The correlation id carried by this scope.</summary>
    public string CorrelationId { get; private init; } = string.Empty;

    /// <summary>
    /// Begins a scope named after the message type, tagged and scoped with the correlation id. A
    /// telemetry failure here must never propagate into business code (AC-06), so activity start is
    /// best-effort.
    /// </summary>
    public static CorrelationScope Begin(string operationName, string correlationId, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(logger);

        var effectiveId = string.IsNullOrWhiteSpace(correlationId) ? Guid.CreateVersion7().ToString() : correlationId;

        var activity = Telemetry.Source.StartActivity(operationName, ActivityKind.Consumer);
        activity?.SetTag("correlation.id", effectiveId);

        var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["correlation_id"] = effectiveId,
        });

        return new CorrelationScope(activity, logScope) { CorrelationId = effectiveId };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _logScope?.Dispose();
        _activity?.Dispose();
    }
}
