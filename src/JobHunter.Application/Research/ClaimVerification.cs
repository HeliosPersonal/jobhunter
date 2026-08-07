using JobHunter.Domain.Research;

namespace JobHunter.Application.Research;

/// <summary>
/// The outcome of verifying one synthesised dossier's claims against its fetched sources: the claims that
/// cited a real fetched URL — each paired to the exact <see cref="ResearchSource"/> that substantiates it —
/// and the count of claims discarded because their cited URL was never fetched (research-schema §Citation
/// verification). Discarded claims are counted, never returned: an uncited claim leaves no trace but the tally.
/// </summary>
public sealed record ClaimVerification(
    IReadOnlyList<VerifiedClaim> Verified,
    int Discarded);

/// <summary>
/// A claim whose cited URL was proven to be a member of this dossier's fetched set, carried alongside the
/// resolved <see cref="ResearchSource"/> so the orchestrator (T08) can construct a <see cref="ResearchClaim"/>
/// — which takes a source object, not a bare id, so invariant 5 holds by construction. The claim's own
/// <see cref="Category"/> is authoritative and need not equal the source's category (a news page can
/// substantiate a layoffs claim).
/// </summary>
public sealed record VerifiedClaim(
    ResearchSource Source,
    ResearchCategory Category,
    string Claim,
    bool IsWarning);
