using JobHunter.Domain.Abstractions;

namespace JobHunter.TestKit;

/// <summary>
/// A deterministic <see cref="IIdGenerator"/> that yields ascending, reproducible GUIDs so a test can
/// assert on exact ids (testing conventions). The ids are still valid v7-shaped GUIDs (time-ordered)
/// but derived from a counter, not the wall clock.
/// </summary>
public sealed class SequentialIdGenerator : IIdGenerator
{
    private int _counter;

    /// <summary>The 1-based count of ids produced so far.</summary>
    public int Count => _counter;

    public Guid NewId()
    {
        var next = Interlocked.Increment(ref _counter);

        // Encode the counter in the last four bytes of an otherwise fixed, ordered GUID so successive
        // ids compare in creation order and are trivially recognisable in a failing assertion.
        Span<byte> bytes = stackalloc byte[16];
        bytes[12] = (byte)(next >> 24);
        bytes[13] = (byte)(next >> 16);
        bytes[14] = (byte)(next >> 8);
        bytes[15] = (byte)next;
        return new Guid(bytes);
    }
}
