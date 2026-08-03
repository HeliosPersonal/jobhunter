using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Reprocessing;

/// <summary>
/// The raw-payload retention job (T09, O3): prune raw postings gone cold for longer than the retention
/// window — 90 days by default, the figure settled in the F1 and global data models. The cutoff is taken
/// from <see cref="IClock"/>, never <c>DateTime.Now</c> (coding-standards §5), so the job is deterministic
/// under test. A posting still referenced by a job's <c>job_aliases</c> row is never removed — that is the
/// provenance a live or closed job depends on — and the repository's <c>NOT EXISTS</c> plus the restrict FK
/// enforce it; this service only decides the cutoff and reports the count.
/// </summary>
public sealed class RetentionService(
    IRawPostingRepository rawPostings,
    IClock clock,
    ILogger<RetentionService> logger)
{
    private readonly IRawPostingRepository _rawPostings = rawPostings ?? throw new ArgumentNullException(nameof(rawPostings));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<RetentionService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>The default retention window: 90 days (O3, settled in the F1 and global data models).</summary>
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(90);

    /// <summary>
    /// Prunes raw postings last seen before <c>now − retention</c> and returns how many were removed.
    /// </summary>
    public async Task<int> PruneAsync(TimeSpan retention, CancellationToken cancellationToken)
    {
        if (retention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retention), retention, "The retention window must be positive.");
        }

        var cutoff = _clock.UtcNow - retention;
        var pruned = await _rawPostings.PruneOlderThanAsync(cutoff, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Retention prune complete: {Pruned} raw posting(s) last seen before {Cutoff:o} removed.",
            pruned, cutoff);
        return pruned;
    }
}
