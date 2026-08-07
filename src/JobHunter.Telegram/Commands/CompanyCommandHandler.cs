using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Reporting;
using JobHunter.Domain.Research;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/company &lt;name&gt;</c> (F8 T09, SAD §6.2, AC-05): the Owner asks about a company by name and gets one
/// of three honest answers. If the company is not in the registry, the reply offers to add it rather than
/// failing. If a fresh dossier exists, it is rendered through the shared <see cref="DossierFormatter"/> — the
/// same layout the digest uses — with its age. If the dossier is stale or absent, a research request is
/// queued for the next cycle and the reply acknowledges that it will be ready with tomorrow's digest.
///
/// <para>Freshness is judged with the domain <see cref="Freshness"/> policy against the injected
/// <see cref="IClock"/>, so the stale/fresh boundary is deterministic and a volatile category (news, layoffs)
/// pulls a refresh forward. The command runs no LLM — research is batched and cost-ceilinged, never inline
/// (ADR-F10-0002) — and touches <strong>no CV</strong> (the CV crosses exactly one boundary, not this one).
/// Every dynamic value reaches the message through the one MarkdownV2 escaper, so a hostile company name or a
/// URL full of markup renders literally and can never break the send.</para>
/// </summary>
internal sealed class CompanyCommandHandler(
    ICompanyResearchQuery research,
    IResearchRequestWriter requests,
    IClock clock,
    ILogger<CompanyCommandHandler> logger) : ICommandHandler
{
    /// <summary>The reason recorded on an on-demand request, so the queue drain can attribute it.</summary>
    private const string OnDemandReason = "on-demand /company";

    private readonly ICompanyResearchQuery _research = research ?? throw new ArgumentNullException(nameof(research));
    private readonly IResearchRequestWriter _requests = requests ?? throw new ArgumentNullException(nameof(requests));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<CompanyCommandHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = request.Arguments?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return [Plain("Which company? Send /company followed by a name.")];
        }

        var lookup = await _research.ResolveByNameAsync(name, cancellationToken).ConfigureAwait(false);
        if (lookup is null)
        {
            // Not in the registry — offer to add it rather than failing (SAD §6.2).
            _logger.LogDebug("/company requested for an unknown company.");
            return [Plain($"I don't have \"{name}\" in the registry yet. Add it and I'll research it.")];
        }

        var dossier = lookup.LatestDossier;
        if (dossier is not null && !IsStale(dossier, _clock.UtcNow))
        {
            // A fresh dossier — present it in the shared card layout with its age.
            return [RenderedMessage.PlainText(DossierFormatter.Format(ToView(lookup.DisplayName, dossier)))];
        }

        // Stale or absent — queue for the next cycle and acknowledge (AC-05).
        await _requests.EnqueueAsync(lookup.CompanyId, OnDemandReason, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("/company queued an on-demand research request.");
        return [Plain($"Queued research on {lookup.DisplayName}. It'll be ready with tomorrow's digest.")];
    }

    // A dossier is stale as soon as it is stale for any category it covered, so a volatile category (news,
    // layoffs) pulls the whole refresh forward. A dossier that covered nothing ages at the default window.
    private static bool IsStale(ResearchDossierSnapshot dossier, DateTimeOffset now)
    {
        var categories = dossier.Claims
            .Select(c => c.Category)
            .Distinct()
            .DefaultIfEmpty(ResearchCategory.Funding);

        return categories.Any(category => Freshness.IsStale(dossier.GeneratedAt, category, now));
    }

    private static DossierView ToView(string companyName, ResearchDossierSnapshot dossier) => new(
        companyName,
        dossier.Summary,
        dossier.GeneratedAt,
        [.. dossier.Claims.Select(c => new DossierClaim(
            c.Category.ToString(), c.Claim, c.ObservedAt, c.SourceUrl, c.IsWarning))],
        [.. dossier.CategoriesUnavailable.Select(c => c.ToString())]);

    // A single plain line, escaped, so the Owner always gets a readable reply even for the error paths.
    private static RenderedMessage Plain(string text) =>
        RenderedMessage.PlainText(MarkdownV2Escaper.Escape(text));
}
