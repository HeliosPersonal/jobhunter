using JobHunter.Application.Common;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Ratings;

/// <summary>
/// Exports weekly precision@10 (F4 T20 done-when 3, D5): the share of the latest rated week's top-ten delivered
/// cards the Owner rated "worth opening", recorded to <c>jobhunter.precision_at_10</c>. It is the empirical
/// counterpart to the golden ranking set — the golden set proves the ranking is stable, this measures whether
/// it is good against the Owner's real judgement — and the counterpart to suppression regret, which asks
/// whether what was hidden was wanted after all.
///
/// <para>It reads through <see cref="IWeeklyPrecisionQuery"/> and records the precision it returns, including a
/// measured zero, so a dashboard sees the figure fall as well as rise; a gauge, so the last week is what the
/// chart watches. A week that has never been rated (a null read) records <em>nothing</em> — null means "not
/// yet measured", and recording it as zero would understate a system that simply has not run a rating round
/// yet. It reads no CV — the CV crosses exactly one boundary, and it is not this one.</para>
/// </summary>
public sealed class WeeklyPrecisionReporter(
    IWeeklyPrecisionQuery query,
    ILogger<WeeklyPrecisionReporter> logger)
{
    private readonly IWeeklyPrecisionQuery _query = query ?? throw new ArgumentNullException(nameof(query));

    private readonly ILogger<WeeklyPrecisionReporter> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Reads the latest rated week's precision, records it as the metric, and returns it — or <c>null</c> when
    /// no week has been rated yet, in which case nothing is recorded.
    /// </summary>
    public async Task<WeeklyPrecision?> ReportAsync(CancellationToken cancellationToken = default)
    {
        var precision = await _query.LatestAsync(cancellationToken).ConfigureAwait(false);
        if (precision is null)
        {
            _logger.LogInformation("No rating round has been opened yet; precision@10 not recorded.");
            return null;
        }

        Telemetry.PrecisionAtTen.Record((double)precision.Precision);
        _logger.LogInformation(
            "Weekly precision@10 for the week of {WeekStart:yyyy-MM-dd}: {Hits}/{Considered} cards rated worth opening.",
            precision.WeekStart, precision.Hits, precision.Considered);

        return precision;
    }
}
