using JobHunter.Domain.Common;

namespace JobHunter.Domain.Sources;

/// <summary>
/// One row per fetch attempt, successful or not (data-model §source_fetch_log, AC-11). The
/// <c>postings_returned</c>/<c>postings_changed</c> pair is the unchanged-content metric; <c>detail</c>
/// is a single line that carries no payload and no secrets (invariant 12).
/// </summary>
public sealed class SourceFetchLog : Entity
{
    public SourceFetchLog(
        Guid id,
        Guid sourceId,
        DateTimeOffset startedAt,
        int durationMs,
        short httpStatus,
        int postingsReturned,
        int postingsChanged,
        FetchOutcome outcome,
        string? detail = null)
        : base(id)
    {
        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("Fetch log source id must not be empty.", nameof(sourceId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(durationMs);
        ArgumentOutOfRangeException.ThrowIfNegative(postingsReturned);
        ArgumentOutOfRangeException.ThrowIfNegative(postingsChanged);

        SourceId = sourceId;
        StartedAt = startedAt;
        DurationMs = durationMs;
        HttpStatus = httpStatus;
        PostingsReturned = postingsReturned;
        PostingsChanged = postingsChanged;
        Outcome = outcome;
        Detail = detail;
    }

    private SourceFetchLog()
    {
    }

    public Guid SourceId { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public int DurationMs { get; private set; }

    /// <summary>0 for transport failures (data-model §source_fetch_log).</summary>
    public short HttpStatus { get; private set; }

    public int PostingsReturned { get; private set; }

    public int PostingsChanged { get; private set; }

    public FetchOutcome Outcome { get; private set; }

    /// <summary>One line, no payload, no secrets.</summary>
    public string? Detail { get; private set; }
}
