using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Common;

namespace JobHunter.Application.Search;

/// <summary>
/// The operator action behind <c>POST /api/admin/sources/{id}/unquarantine</c> (F9 operational endpoints,
/// runbook R4): release a source that a run of fetch failures put into quarantine, so recovery does not
/// require a hand-written <c>UPDATE job_sources</c>. It loads the tracked <see cref="Domain.Sources.JobSource"/>
/// through the write port, applies the aggregate's own <c>ReleaseQuarantine</c> transition and saves —
/// the invariant that only a quarantined source is released lives in the aggregate, not here.
///
/// <para>The outcome is a value, never an exception: an unknown id is <see cref="ReleaseOutcome.NotFound"/>,
/// a source that was not quarantined is <see cref="ReleaseOutcome.NotQuarantined"/> (so the endpoint can
/// answer "nothing to do" rather than pretend it changed something), and a release is
/// <see cref="ReleaseOutcome.Released"/>.</para>
/// </summary>
public sealed class SourceQuarantineService(IJobSourceRepository sources)
{
    private readonly IJobSourceRepository _sources = sources ?? throw new ArgumentNullException(nameof(sources));

    public async Task<Result<ReleaseOutcome>> UnquarantineAsync(
        Guid sourceId,
        CancellationToken cancellationToken = default)
    {
        var source = await _sources.FindAsync(sourceId, cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            return Result<ReleaseOutcome>.Success(ReleaseOutcome.NotFound);
        }

        if (!source.ReleaseQuarantine())
        {
            return Result<ReleaseOutcome>.Success(ReleaseOutcome.NotQuarantined);
        }

        await _sources.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<ReleaseOutcome>.Success(ReleaseOutcome.Released);
    }
}

/// <summary>The result of an unquarantine request: the source was released, was already healthy, or was unknown.</summary>
public enum ReleaseOutcome
{
    Released,
    NotQuarantined,
    NotFound,
}
