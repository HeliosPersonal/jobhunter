using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Research;

namespace JobHunter.Application.Research;

/// <summary>
/// Turns one company's research cycle into a verified <see cref="CompanyResearch"/> dossier (SAD §6.1) — the
/// deterministic core of the orchestration flow. It stores every fetched document as a <see cref="ResearchSource"/>
/// <em>before</em> verification, so "did the model invent this" becomes a set-membership check rather than a
/// judgement (QG-1); runs the synthesiser's claims through the <see cref="ClaimVerifier"/>, keeping only those
/// whose cited URL was actually fetched and counting the rest as discarded; and records every category with no
/// surviving claim as unavailable, because a known absence is information the Owner should see (AC-07).
///
/// <para>Warnings-first ordering and the "every claim rests on a recorded source" invariant are enforced by
/// the aggregate itself, so the assembler's whole job is to feed it verified material and the right identity.
/// It reads no clock and opens no socket — the handler supplies the generation instant and the ids come from
/// <see cref="IIdGenerator"/> — so it stays a pure function of its <see cref="ResearchDossierInput"/>.</para>
/// </summary>
public sealed class ResearchDossierAssembler(ClaimVerifier verifier, IIdGenerator ids)
{
    private readonly ClaimVerifier _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));

    /// <summary>
    /// Assembles the dossier: fetched documents become sources, verified claims become stored claims resting
    /// on them, discarded claims are counted, and the categories with no surviving claim are named unavailable.
    /// </summary>
    public CompanyResearch Assemble(ResearchDossierInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sources = input.Documents
            .Select(d => new ResearchSource(
                _ids.NewId(),
                d.Category,
                d.Document.Url,
                d.Document.Title,
                d.Document.Text.Length,
                d.Document.ObservedAt))
            .ToList();

        var verification = _verifier.Verify(sources, input.Synthesis.Claims);

        var claims = verification.Verified
            .Select(v => new ResearchClaim(_ids.NewId(), v.Source, v.Category, v.Claim, v.IsWarning))
            .ToList();

        // AC-07: every category with no surviving claim is recorded as unavailable, not silently omitted — a
        // category whose only claim was discarded is as unavailable as one that never fetched a document.
        var covered = claims.Select(c => c.Category).ToHashSet();
        var unavailable = Enum.GetValues<ResearchCategory>()
            .Where(c => !covered.Contains(c))
            .ToList();

        return new CompanyResearch(
            _ids.NewId(),
            input.CompanyId,
            input.RunId,
            input.Synthesis.Summary,
            sources,
            claims,
            unavailable,
            verification.Discarded,
            input.PromptVersion,
            input.GeneratedAt);
    }
}
