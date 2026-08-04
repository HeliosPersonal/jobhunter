using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Profiles;

/// <summary>
/// The re-staling and re-match half of a CV change (ADR-F4-0002, AC-08, F4 SAD §6.3). Run inline by the
/// upload service the moment a new version is activated — the Api host has no message bus, so activation is
/// a synchronous owner-scoped write, not a published event. Two effects, both of them corrections:
///
/// <list type="number">
///   <item>Every current match computed against an <em>older</em> CV version is marked
///   <c>is_current = false</c> (AC-08). Matches are <strong>marked, never deleted</strong> — an old match
///   with <c>is_current = false</c> is the honest record of what yesterday's digest was told.</item>
///   <item>Every live job first seen within the re-match window (default 30 days) is enqueued for re-match
///   against the new version, at the cheap tier, so the next Run re-assesses it. Enqueue is idempotent per
///   job, so re-uploading a CV twice before a Run drains the queue does not queue a job twice.</item>
/// </list>
///
/// <para>Re-uploading identical content never reaches here: the upload service recognises the content hash
/// and returns the existing version without activating, so no re-staling and no re-match are triggered
/// (T09 done-when). The scheduler selects <strong>nothing about the Owner</strong> — the CV crosses exactly
/// one boundary, and it is not this one.</para>
/// </summary>
public sealed class ReMatchScheduler(
    IMatchRepository matches,
    ILiveJobsQuery liveJobs,
    IReMatchBacklog queue,
    ReMatchOptions options,
    IIdGenerator ids,
    IClock clock,
    ILogger<ReMatchScheduler> logger)
{
    private readonly IMatchRepository _matches = matches ?? throw new ArgumentNullException(nameof(matches));
    private readonly ILiveJobsQuery _liveJobs = liveJobs ?? throw new ArgumentNullException(nameof(liveJobs));
    private readonly IReMatchBacklog _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    private readonly ReMatchOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<ReMatchScheduler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Re-stales matches from older CV versions and queues the recent live jobs for re-match against
    /// <paramref name="activeCvVersionId"/> — the version that was just activated. Returns how many matches
    /// were staled and how many jobs were newly queued.
    /// </summary>
    public async Task<ReMatchOutcome> ScheduleAsync(
        Guid activeCvVersionId,
        CancellationToken cancellationToken = default)
    {
        if (activeCvVersionId == Guid.Empty)
        {
            throw new ArgumentException("The activated CV version id is required.", nameof(activeCvVersionId));
        }

        // AC-08: every current match made against a previous version is marked stale in one UPDATE. Nothing
        // is deleted — the row survives with is_current = false.
        var staled = await _matches
            .MarkNotCurrentExceptCvVersionAsync(activeCvVersionId, cancellationToken)
            .ConfigureAwait(false);

        var now = _clock.UtcNow;
        var windowStart = now - _options.Window;

        // Exactly the live jobs first seen at or after the window start (ADR-F4-0002). DiscoveredSinceAsync
        // returns only Live jobs — a closed or quarantined job is never re-matched — first seen >= since.
        var recent = await _liveJobs.DiscoveredSinceAsync(windowStart, cancellationToken).ConfigureAwait(false);

        var queued = 0;
        foreach (var job in recent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = new ReMatchQueueItem(_ids.NewId(), job.Id, activeCvVersionId, now);
            if (await _queue.EnqueueAsync(item, cancellationToken).ConfigureAwait(false))
            {
                queued++;
            }
        }

        _logger.LogInformation(
            "CV version {CvVersionId} activated: staled {Staled} match(es) from older versions and queued {Queued} " +
            "live job(s) first seen since {WindowStart:o} for cheap-tier re-match.",
            activeCvVersionId, staled, queued, windowStart);

        return new ReMatchOutcome(staled, queued);
    }
}

/// <summary>
/// The tally of a CV activation's re-match effects (T09): how many older-version matches were staled and
/// how many live jobs were newly queued for re-match. Carries no CV content — only counts.
/// </summary>
public readonly record struct ReMatchOutcome(int StaledMatches, int QueuedJobs);
