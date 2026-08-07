using JobHunter.Application.Common;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Preferences;

/// <summary>
/// Exports suppression regret (F7 T09 done-when 5, risk D3): the count of the latest Run's suppressed jobs the
/// Owner then acted on, recorded to <c>jobhunter.preferences.suppression_regret</c>. It is the counterweight to
/// precision@10 — precision asks whether what was shown was wanted, regret asks whether what was hidden was
/// wanted after all — and a rising regret is the signal the learned model is over-suppressing (invariant 11).
///
/// <para>It reads through <see cref="ISuppressionRegretQuery"/> and records whatever it returns, including
/// zero, so a dashboard sees regret fall as well as rise; a gauge, so the last value is what an alert watches.
/// It reads no CV — the CV crosses exactly one boundary, and it is not this one.</para>
/// </summary>
public sealed class SuppressionRegretReporter(
    ISuppressionRegretQuery query,
    ILogger<SuppressionRegretReporter> logger)
{
    private readonly ISuppressionRegretQuery _query = query ?? throw new ArgumentNullException(nameof(query));

    private readonly ILogger<SuppressionRegretReporter> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Reads the latest Run's regret count, records it as the metric, and returns the value recorded.</summary>
    public async Task<int> ReportAsync(CancellationToken cancellationToken = default)
    {
        var regret = await _query.LatestRunRegretCountAsync(cancellationToken).ConfigureAwait(false);

        Telemetry.SuppressionRegret.Record(regret);
        _logger.LogInformation("Suppression regret for the latest Run: {Regret} suppressed jobs the Owner acted on.", regret);

        return regret;
    }
}
