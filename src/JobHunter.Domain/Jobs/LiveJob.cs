namespace JobHunter.Domain.Jobs;

/// <summary>
/// A flat read model of a live job (data-model §jobs). Returned by the live-jobs read query — the "new
/// since the last Run cut-off" access pattern served by the partial index <c>idx_jobs_first_seen</c>. It
/// carries only what a downstream consumer needs to select and route a job; the full aggregate is loaded
/// through the write repository when a mutation is required. The published <see cref="Title"/> is carried;
/// the never-displayed normalised form is not.
/// </summary>
public sealed record LiveJob(
    Guid Id,
    Guid CompanyId,
    string Title,
    string? Seniority,
    string RemotePolicy,
    string EmploymentType,
    string ApplyUrl,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);
