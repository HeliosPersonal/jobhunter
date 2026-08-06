using JobHunter.Domain.Applications;

namespace JobHunter.Application.Applications;

/// <summary>
/// A request to move a tracked job's application to <paramref name="ToStatus"/> (F6 T09) — the shared write
/// path behind both the API <c>POST …/status</c> and the Telegram <c>/pipeline</c> status callbacks. Keyed by
/// <see cref="JobId"/>, the same job-scoped surface every F6 handler uses (QG-1); the API, which addresses an
/// application by id, resolves the id to its job before dispatching.
///
/// <para><paramref name="Source"/> is what makes the two callers distinguishable in the history (done-when 4):
/// the API passes <see cref="TransitionSource.Api"/>, Telegram passes <see cref="TransitionSource.Telegram"/>.
/// <paramref name="Detail"/> is the optional free-text note the API accepts alongside the change (never logged).</para>
/// </summary>
/// <param name="JobId">The job whose application is moved.</param>
/// <param name="ToStatus">The target status the transition is evaluated against.</param>
/// <param name="Source">Who drove the change — recorded verbatim on the transition.</param>
/// <param name="OccurredAt">When the change happened (from <c>IClock</c>, never <c>DateTime.Now</c>).</param>
/// <param name="Detail">An optional note recorded on the transition; <c>null</c> when none was given.</param>
public sealed record ChangeApplicationStatusCommand(
    Guid JobId,
    ApplicationStatus ToStatus,
    TransitionSource Source,
    DateTimeOffset OccurredAt,
    string? Detail = null);
