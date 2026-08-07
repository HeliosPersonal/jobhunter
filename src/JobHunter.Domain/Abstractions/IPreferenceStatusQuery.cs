using JobHunter.Domain.Reporting;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read behind the <c>/prefs</c> below-threshold line (F10 T08): the metadata of the most recent preference
/// fit — its signal count and whether it is active — so the command can say how many more signals learning needs
/// before it shapes a ranking. It is deliberately separate from <see cref="IPreferenceModelRepository"/>: that is
/// the aggregate write repository, whose <c>FindActiveAsync</c> returns nothing when no model has been activated,
/// which is exactly the case where <c>/prefs</c> still needs the latest fit's count. A thin metadata read keeps
/// that concern out of the write path and off the repository's fakes.
/// </summary>
public interface IPreferenceStatusQuery
{
    /// <summary>
    /// The latest fitted model's metadata, or null when no model has ever been fitted, so the command states the
    /// full threshold is outstanding rather than rendering a zero.
    /// </summary>
    Task<PreferenceStatus?> LatestAsync(CancellationToken cancellationToken = default);
}
