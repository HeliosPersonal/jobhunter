using Hangfire;
using JobHunter.Domain.Abstractions;

namespace JobHunter.Infrastructure.Scheduling;

/// <summary>
/// The Hangfire-backed <see cref="IOperationScheduler"/> (F9 operational endpoints, ADR-0004). Hangfire's
/// PostgreSQL storage is composed in every host, so the Api enqueues a job here and the Worker's
/// background server runs it — the endpoint never blocks on a rebuild or a reprocess and returns the
/// Hangfire job id as the operation id the operator can quote. It is the one place a scheduler type is
/// touched; the endpoints depend on the port, keeping them free of Hangfire.
/// </summary>
internal sealed class HangfireOperationScheduler(IBackgroundJobClient jobs) : IOperationScheduler
{
    private readonly IBackgroundJobClient _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));

    public string EnqueueReindex() =>
        _jobs.Enqueue<IndexRebuildTrigger>(trigger => trigger.RunAsync());

    public string EnqueueReprocess(DateTimeOffset firstSeenFrom) =>
        _jobs.Enqueue<ReprocessTrigger>(trigger => trigger.RunAsync(firstSeenFrom));

    public string EnqueueDailyRun() =>
        _jobs.Enqueue<DailyRunTrigger>(trigger => trigger.PublishAsync());

    public string EnqueueDigestDelivery() =>
        _jobs.Enqueue<DigestDeliveryTrigger>(trigger => trigger.PublishAsync());
}
