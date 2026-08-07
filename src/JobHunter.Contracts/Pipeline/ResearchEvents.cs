namespace JobHunter.Contracts.Pipeline;

/// <summary>
/// A company research dossier was assembled, verified and stored (event-catalog §3, F8 SAD §6.1). Published
/// once per dossier in the same transaction that writes it — the idempotency key is <c>(RunId, CompanyId)</c>,
/// and the one-dossier-per-<c>(company, run)</c> database constraint makes a second publication for the same
/// pair impossible, so the key never collides. Consumed by F5 reporting, which folds the dossier's warnings
/// and claim count into the day's digest. <see cref="ClaimCount"/> is the number of <em>verified</em> claims
/// stored — a dossier that fetched nothing publishes with a zero count rather than staying silent, so the
/// digest still knows the company was researched.
/// </summary>
public sealed record ResearchCompleted(
    Guid RunId,
    Guid CompanyId,
    Guid ResearchId,
    int ClaimCount,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
