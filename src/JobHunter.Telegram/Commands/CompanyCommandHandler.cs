using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Reporting;
using JobHunter.Domain.Research;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/company &lt;name-or-domain&gt;</c> (catalogue §Company, AC-11): a <strong>read-only</strong> lookup that
/// answers the Owner honestly in one of four ways. Resolution is forgiving — the display name (<c>Stripe</c>),
/// the domain (<c>stripe.com</c>) and the bare label (<c>stripe</c>) all resolve through
/// <see cref="ICompanyResearchQuery.ResolveCandidatesAsync"/>. If nothing matches, the reply offers to add the
/// company rather than returning empty. If exactly one company matches, its dossier is rendered through the
/// shared <see cref="DossierFormatter"/> with its age when fresh; when the dossier is stale or absent, the reply
/// offers <c>/research</c> — this command never queues a write, because the queue is <c>/research</c>'s job
/// (catalogue §Company · State ✎). If more than one company matches, the ambiguity is surfaced so the Owner can
/// pick, never silently resolved to the first.
///
/// <para>Freshness is judged with the domain <see cref="Freshness"/> policy against the injected
/// <see cref="IClock"/>, so the stale/fresh boundary is deterministic. The command runs no LLM and touches
/// <strong>no CV</strong> (the CV crosses exactly one boundary, not this one). Every dynamic value reaches the
/// message through the one MarkdownV2 escaper, so a hostile company name renders literally.</para>
/// </summary>
internal sealed class CompanyCommandHandler(
    ICompanyResearchQuery research,
    IClock clock,
    ILogger<CompanyCommandHandler> logger) : ICommandHandler
{
    private readonly ICompanyResearchQuery _research = research ?? throw new ArgumentNullException(nameof(research));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<CompanyCommandHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = request.Arguments?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return [Plain("Which company? Send /company followed by a name or domain.")];
        }

        var candidates = await _research.ResolveCandidatesAsync(query, cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            // Not in the registry — offer to add it rather than failing (AC-11).
            _logger.LogDebug("/company requested for an unknown company.");
            return [Plain($"I don't have \"{query}\" in the registry yet. Add it and I'll research it.")];
        }

        if (candidates.Count > 1)
        {
            // A genuine ambiguity: name each match so the Owner can pick, never resolve to the first silently.
            _logger.LogDebug("/company matched more than one company; offering the choices.");
            var lines = candidates.Select(c => $"• {c.DisplayName}");
            return [Plain($"\"{query}\" matches more than one company:\n" + string.Join("\n", lines))];
        }

        var only = candidates[0];
        var dossier = only.LatestDossier;
        if (dossier is null)
        {
            // Known but never researched — read-only, so offer /research rather than queueing here.
            return [Plain($"I know {only.DisplayName}, but haven't researched it yet. Send /research {only.DisplayName} to queue a dossier.")];
        }

        // Present the dossier in the shared card layout; when it is stale, append an offer to refresh.
        var card = RenderedMessage.PlainText(DossierFormatter.Format(ToView(only.DisplayName, dossier)));
        if (!IsStale(dossier, _clock.UtcNow))
        {
            return [card];
        }

        return [card, Plain($"This dossier is getting old. Send /research {only.DisplayName} to refresh it.")];
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
