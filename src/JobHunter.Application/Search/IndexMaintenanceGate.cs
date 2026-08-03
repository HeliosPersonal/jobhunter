namespace JobHunter.Application.Search;

/// <summary>
/// The single-holder gate that serialises the two index-maintenance operations (F9-T08, test-plan
/// "reconcile during an active rebuild"). A full rebuild drops and recreates the collection, so a reconcile
/// that ran against it at the same time would compare a half-filled index and re-index needlessly — worse,
/// two writers streaming the live set at once waste the round trips a rebuild is budgeted for. The rebuild
/// takes the gate for its whole duration; a reconcile that cannot take it skips and logs rather than
/// blocking a background job on a lock (SAD §6.3). It is in-process because both operations run in the one
/// Worker that owns the Hangfire server, so there is exactly one contender host by construction.
/// </summary>
public sealed class IndexMaintenanceGate
{
    private int _held;

    /// <summary>
    /// Attempts to take the gate. Returns a lease to dispose on completion when it was free, or
    /// <c>null</c> when another operation already holds it — the caller then skips.
    /// </summary>
    public Lease? TryAcquire()
    {
        // A 0 -> 1 transition wins the gate; any other observed value means it is already held.
        return Interlocked.CompareExchange(ref _held, 1, 0) == 0 ? new Lease(this) : null;
    }

    private void Release() => Interlocked.Exchange(ref _held, 0);

    /// <summary>The held gate; disposing it releases the gate for the next operation.</summary>
    public sealed class Lease : IDisposable
    {
        private IndexMaintenanceGate? _gate;

        internal Lease(IndexMaintenanceGate gate) => _gate = gate;

        public void Dispose()
        {
            // Idempotent: a double dispose must not release a gate a later operation has since taken.
            Interlocked.Exchange(ref _gate, null)?.Release();
        }
    }
}
