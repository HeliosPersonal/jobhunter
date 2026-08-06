using JobHunter.Domain.Preferences;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The write port over <c>signals</c> (F7 data-model §signals). F5 (card actions) and F6 (outcomes) capture
/// signals through it; F7 owns the schema and reads them for fitting. Capture is idempotent by the unique
/// <c>(job_id, kind, occurred_at)</c> constraint — a redelivered action produces no second signal — so a
/// handler that runs twice records one signal and a caller can tell which run was the first.
/// </summary>
public interface ISignalRepository
{
    /// <summary>
    /// Records <paramref name="signal"/>, returning <c>true</c> when this call inserted it and <c>false</c>
    /// when an identical signal (same job, kind and moment) was already present. Idempotent: the second call
    /// is a no-op, not a duplicate and not an error.
    /// </summary>
    Task<bool> TryCaptureAsync(Signal signal, CancellationToken cancellationToken = default);
}
