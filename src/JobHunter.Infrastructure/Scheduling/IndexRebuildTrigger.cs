using JobHunter.Application.Search;
using Microsoft.Extensions.Logging;

namespace JobHunter.Infrastructure.Scheduling;

/// <summary>
/// The Hangfire job body for an operator-requested full index rebuild (F9 operational endpoints, runbook
/// R8). The <c>POST /api/admin/search/reindex</c> endpoint enqueues this rather than rebuilding inline —
/// a rebuild takes minutes — so the request returns an operation id immediately and the work runs on the
/// Worker's Hangfire server. Like the reconcile trigger it resolves the Application service and runs it
/// directly; the drop-recreate-stream logic and the maintenance gate live in that service, which is
/// unit-tested without Hangfire. A rebuild failure is a value the service returns, logged here rather
/// than thrown into the Hangfire server (QG-3).
/// </summary>
internal sealed class IndexRebuildTrigger(IndexRebuildService rebuild, ILogger<IndexRebuildTrigger> logger)
{
    private readonly IndexRebuildService _rebuild = rebuild ?? throw new ArgumentNullException(nameof(rebuild));
    private readonly ILogger<IndexRebuildTrigger> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task RunAsync()
    {
        var result = await _rebuild.RebuildAsync().ConfigureAwait(false);
        if (result.IsFailure)
        {
            _logger.LogError("Operator-requested index rebuild failed: {Error}.", result.Error.Code);
            return;
        }

        var report = result.Value;
        if (report.Skipped)
        {
            _logger.LogWarning("Operator-requested index rebuild skipped: another maintenance operation holds the gate.");
            return;
        }

        _logger.LogInformation(
            "Operator-requested index rebuild complete: {Documents} documents in {Elapsed}, within budget {WithinBudget}.",
            report.Documents, report.Elapsed, report.WithinBudget);
    }
}
