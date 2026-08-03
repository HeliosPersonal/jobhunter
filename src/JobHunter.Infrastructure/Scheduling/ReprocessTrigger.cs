using JobHunter.Application.Reprocessing;
using Microsoft.Extensions.Logging;

namespace JobHunter.Infrastructure.Scheduling;

/// <summary>
/// The Hangfire job body for an operator-requested history reprocess (F9 operational endpoints, F2 AC-09,
/// runbook R4). The <c>POST /api/admin/jobs/reprocess</c> endpoint enqueues this with the window's lower
/// bound rather than reprocessing inline — a full recompute over stored payloads can take minutes — so the
/// request returns an operation id and the work runs on the Worker's Hangfire server. The zero-network
/// recompute logic lives in the Application <see cref="ReprocessingService"/>, which is unit-tested
/// directly; this body is the thin schedule seam that runs it and logs the tally.
/// </summary>
internal sealed class ReprocessTrigger(ReprocessingService reprocessing, ILogger<ReprocessTrigger> logger)
{
    private readonly ReprocessingService _reprocessing =
        reprocessing ?? throw new ArgumentNullException(nameof(reprocessing));

    private readonly ILogger<ReprocessTrigger> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task RunAsync(DateTimeOffset firstSeenFrom)
    {
        var report = await _reprocessing.ReprocessAsync(firstSeenFrom, CancellationToken.None).ConfigureAwait(false);
        _logger.LogInformation(
            "Operator-requested reprocess from {FirstSeenFrom:o} complete: {Examined} examined, {Unchanged} unchanged, " +
            "{Superseded} superseded, {Failed} failed.",
            firstSeenFrom, report.Examined, report.Unchanged, report.Superseded, report.Failed);
    }
}
