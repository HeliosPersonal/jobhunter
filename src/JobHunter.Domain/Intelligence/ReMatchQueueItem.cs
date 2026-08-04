using JobHunter.Domain.Common;
using JobHunter.Domain.Pipeline;

namespace JobHunter.Domain.Intelligence;

/// <summary>
/// One job queued for re-match after a CV change (ADR-F4-0002, data-model §cv_versions). When a new CV
/// version is activated, every live job first seen in the re-match window is enqueued here so the next Run
/// re-assesses it against the new CV — a correction, not a first judgement, so it carries
/// <see cref="ModelTier.Cheap"/>. The old match is <em>marked</em> stale, never deleted; this row is the
/// forward instruction to produce a fresh one.
///
/// <para>Identity is the surrogate <see cref="Entity.Id"/>, but the queue is deduplicated on
/// <c>(job_id) WHERE NOT consumed</c>: re-uploading a CV twice before a Run drains the queue does not
/// enqueue a job twice. <see cref="Consumed"/> is the only mutable field — the next Run flips it once it
/// has folded the job into its matching scope, so a drained item is not re-matched forever.</para>
/// </summary>
public sealed class ReMatchQueueItem : Entity
{
    /// <summary>
    /// Enqueues <paramref name="jobId"/> for re-match against <paramref name="cvVersionId"/>. The tier is
    /// fixed at <see cref="ModelTier.Cheap"/> — re-matching corrects an existing judgement rather than
    /// forming a new one, so it does not warrant the deep tier (ADR-F4-0002).
    /// </summary>
    public ReMatchQueueItem(
        Guid id,
        Guid jobId,
        Guid cvVersionId,
        DateTimeOffset enqueuedAt)
        : base(id)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("A re-match item must reference a Job.", nameof(jobId));
        }

        if (cvVersionId == Guid.Empty)
        {
            throw new ArgumentException("A re-match item must reference the CV version it re-matches against.", nameof(cvVersionId));
        }

        JobId = jobId;
        CvVersionId = cvVersionId;
        Tier = ModelTier.Cheap;
        EnqueuedAt = enqueuedAt;
        Consumed = false;
    }

    private ReMatchQueueItem()
    {
    }

    public Guid JobId { get; private init; }

    /// <summary>The CV version this job is queued to be re-matched against — the newly activated one.</summary>
    public Guid CvVersionId { get; private init; }

    /// <summary>Always <see cref="ModelTier.Cheap"/>: a re-match is a correction, ledgered at the cheap tier.</summary>
    public ModelTier Tier { get; private init; }

    public DateTimeOffset EnqueuedAt { get; private init; }

    /// <summary>True once the next Run has folded this job into its matching scope; drained items are not re-matched again.</summary>
    public bool Consumed { get; private set; }

    /// <summary>Marks this item drained after the Run that re-matches it has taken it into scope.</summary>
    public void MarkConsumed() => Consumed = true;
}
