using JobHunter.Domain.Research;

namespace JobHunter.Application.Research;

/// <summary>
/// A claim as the synthesiser returned it, before verification (research-schema §Citation verification).
/// This is the parsed-but-not-yet-trusted shape: the model can always cite a plausible <see cref="SourceUrl"/>
/// it invented, so this type deliberately carries a bare URL string, not a <see cref="ResearchSource"/> — a
/// <see cref="ResearchClaim"/> cannot even be constructed until the URL is proven to be one this dossier
/// actually fetched. The claude layer maps its wire DTO onto this, so <c>JobHunter.Application</c> depends on
/// its own type rather than on <c>JobHunter.Claude</c> (mirroring how enrichment parsing crosses the boundary).
/// </summary>
public sealed record UnverifiedClaim(
    ResearchCategory Category,
    string Claim,
    string SourceUrl,
    bool IsWarning);
