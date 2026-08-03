namespace JobHunter.Domain.Jobs;

/// <summary>
/// The provenance trail: one raw posting that contributed to a job, from which source and over what
/// window it was seen (data-model §job_aliases). Aliases are <strong>never deleted</strong> — this table
/// is the evidence for diagnosing a suspected bad merge, and its per-alias <see cref="LastSeenAt"/> is
/// what drives closure: a job closes only when <em>every</em> alias has gone stale, not when one has.
/// </summary>
public sealed class JobAlias
{
    public JobAlias(
        Guid jobId,
        Guid rawPostingId,
        Guid sourceId,
        DateTimeOffset firstSeenAt,
        DateTimeOffset lastSeenAt)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("Alias job id must not be empty.", nameof(jobId));
        }

        if (rawPostingId == Guid.Empty)
        {
            throw new ArgumentException("Alias raw posting id must not be empty.", nameof(rawPostingId));
        }

        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("Alias source id must not be empty.", nameof(sourceId));
        }

        JobId = jobId;
        RawPostingId = rawPostingId;
        SourceId = sourceId;
        FirstSeenAt = firstSeenAt;
        LastSeenAt = lastSeenAt;
    }

    private JobAlias()
    {
    }

    public Guid JobId { get; private set; }

    public Guid RawPostingId { get; private set; }

    public Guid SourceId { get; private set; }

    public DateTimeOffset FirstSeenAt { get; private set; }

    /// <summary>Bumped whenever this posting is seen again; drives per-alias staleness.</summary>
    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>Records that this posting was seen again, never moving the timestamp backwards.</summary>
    internal void Touch(DateTimeOffset seenAt)
    {
        if (seenAt > LastSeenAt)
        {
            LastSeenAt = seenAt;
        }
    }
}
