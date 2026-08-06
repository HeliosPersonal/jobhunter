using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Preferences;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// Stages an outcome <see cref="Signal"/> into the shared write <see cref="JobHunterDbContext"/> (F6 T08,
/// AC-08). Unlike <see cref="SignalRepository"/> — F5's card-action writer, which opens its own connection and
/// commits immediately — this <see cref="IOutcomeSignalWriter"/> adds the signal to the same context the
/// owner-action handler mutates the application through, and does <strong>not</strong> save. The handler's one
/// <c>SaveChanges</c> then commits the transition and the signal together, so the weighted evidence and the
/// status change are all-or-nothing (SAD §6.1): a signal is never written for a transition that rolled back.
///
/// <para><see cref="IsStaged"/> inspects the change tracker for a signal already pending with the same
/// <c>(job_id, kind, occurred_at)</c>, so a redelivered outcome within the same unit of work stages no
/// duplicate — the in-memory belt to the database's unique <c>uq_signals_action</c> braces.</para>
/// </summary>
internal sealed class OutcomeSignalWriter(JobHunterDbContext context) : IOutcomeSignalWriter
{
    private readonly JobHunterDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public void Stage(Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        _context.Set<Signal>().Add(signal);
    }

    public bool IsStaged(Guid jobId, SignalKind kind, DateTimeOffset occurredAt) =>
        _context.ChangeTracker.Entries<Signal>()
            .Any(e => e.State == EntityState.Added
                && e.Entity.JobId == jobId
                && e.Entity.Kind == kind
                && e.Entity.OccurredAt == occurredAt);
}
