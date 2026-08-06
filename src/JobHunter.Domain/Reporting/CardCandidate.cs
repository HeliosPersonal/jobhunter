namespace JobHunter.Domain.Reporting;

/// <summary>
/// A delivered card, reduced to what a callback needs to resolve and act on (F5 T10, contract §Callback
/// payloads). A callback carries only a signed short id — <c>base64url(HMAC(cardKey, botSecret)[0..8])</c>
/// — so the handler HMAC-matches that id against the <see cref="Key"/> of each recent candidate to recover
/// the <see cref="JobId"/> the action applies to and the <see cref="ApplyUrl"/> the Open button links to.
/// It carries nothing about the Owner: the CV crosses exactly one boundary, and it is not this one.
/// </summary>
/// <param name="Key">The card's deterministic idempotence key, the HMAC input the short id signs.</param>
/// <param name="JobId">The job the card presents — the action's subject.</param>
/// <param name="ApplyUrl">The posting's apply link, so the resolved card can offer the Open button.</param>
public sealed record CardCandidate(CardKey Key, Guid JobId, string ApplyUrl);
