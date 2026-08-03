using JobHunter.Application.Search;
using Microsoft.Extensions.Logging;

namespace JobHunter.Infrastructure.Scheduling;

/// <summary>
/// The Hangfire job body for the nightly index reconcile (F9-T08, SAD §6.3). Hangfire invokes
/// <see cref="RunAsync"/> on the 04:00 cron; unlike the pipeline triggers it does not publish a message —
/// reconcile is a self-contained maintenance operation, not a pipeline stage — so it resolves the
/// <see cref="IndexReconcileService"/> and runs it directly. All of the logic (the count comparison, the
/// drift metric, the re-index of the live set and the gate that makes it skip during a rebuild) lives in
/// that Application service, which is unit-tested without Hangfire; this body is the thin schedule seam.
/// A reconcile failure is already a value the service returns, so the body logs it rather than letting a
/// throw fault the Hangfire server (QG-3).
/// </summary>
internal sealed class IndexReconcileTrigger(IndexReconcileService reconcile, ILogger<IndexReconcileTrigger> logger)
{
    private readonly IndexReconcileService _reconcile = reconcile ?? throw new ArgumentNullException(nameof(reconcile));
    private readonly ILogger<IndexReconcileTrigger> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task RunAsync()
    {
        var result = await _reconcile.ReconcileAsync().ConfigureAwait(false);
        if (result.IsFailure)
        {
            _logger.LogError("Nightly index reconcile failed: {Error}.", result.Error.Code);
            return;
        }

        var report = result.Value;
        if (report.Skipped)
        {
            _logger.LogInformation("Nightly index reconcile skipped (a rebuild holds the gate).");
            return;
        }

        _logger.LogInformation(
            "Nightly index reconcile complete: drift {Drift:P2}, drifted {Drifted}, re-indexed {Reindexed}.",
            report.Drift, report.Drifted, report.Reindexed);
    }
}
