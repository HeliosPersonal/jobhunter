using JobHunter.Domain.Jobs;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// Matches a sample of pre-match-excluded jobs at the <em>cheap</em> tier and returns the score each would
/// have reached had it not been filtered out (F4 T21, ADR-F4-0003). This is the falsification half of the
/// pre-match filter: the sampler thresholds these would-be scores against the presentation threshold, and any
/// that clear it are jobs a filter rule wrongly removed. The port lives in Domain so the Application sampler
/// depends on it rather than on <c>JobHunter.Claude</c>; the implementation renders the same match prompt as
/// the deep tier — <strong>the one boundary the CV crosses</strong> — but submits it at cheap tier, because a
/// weekly control does not need deep-tier judgement to catch a rule excluding obviously-wanted work.
/// </summary>
public interface IRegretMatcher
{
    /// <summary>
    /// Scores <paramref name="jobs"/> at cheap tier, returning one <see cref="RegretMatch"/> per job that
    /// produced a usable result. A job whose match could not be parsed is omitted rather than scored zero, so a
    /// provider hiccup never masquerades as "the filter was right". Returns an empty list when
    /// <paramref name="jobs"/> is empty.
    /// </summary>
    Task<IReadOnlyList<RegretMatch>> MatchAsync(
        IReadOnlyList<MatchJobContent> jobs, CancellationToken cancellationToken = default);
}

/// <summary>
/// One sampled excluded job and the score it would have reached had the pre-match filter not removed it. A
/// <see cref="WouldBeScore"/> at or above the presentation threshold is a regret — a job a rule wrongly
/// filtered (ADR-F4-0003).
/// </summary>
public sealed record RegretMatch(Guid JobId, decimal WouldBeScore);
