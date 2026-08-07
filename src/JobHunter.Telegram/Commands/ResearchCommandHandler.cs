using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Reporting;
using JobHunter.Domain.Research;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/research &lt;name-or-domain&gt;</c> (catalogue §Company · State ✎, F8 AC-05): the command that
/// <strong>owns the on-demand research queue write</strong> — the twin of the read-only <c>/company</c>.
/// Resolution is the same forgiving lookup through <see cref="ICompanyResearchQuery.ResolveCandidatesAsync"/>,
/// so a name, a domain and a bare label all match; an unknown company is offered as a registry addition and an
/// ambiguous query offers every match, and in neither case is anything queued — a write to the wrong company
/// costs a research cycle. When exactly one company resolves, an absent or stale dossier is queued through
/// <see cref="IResearchRequestWriter.EnqueueAsync"/> for the next cycle; a dossier that is still fresh is
/// <em>not</em> re-queued — its freshness is reported so a needless refresh is visible before it is paid for.
///
/// <para>Research is batched and cost-ceilinged, never interactive, so a queued request is acknowledged with
/// "tomorrow's digest", never an inline result. Freshness is judged with the domain <see cref="Freshness"/>
/// policy against the injected <see cref="IClock"/>, so the stale/fresh boundary is deterministic. No LLM, and
/// <strong>no CV</strong> (the CV crosses exactly one boundary, not this one). Every dynamic value reaches the
/// reply through the one MarkdownV2 escaper, so a hostile company name renders literally.</para>
/// </summary>
internal sealed class ResearchCommandHandler(
    ICompanyResearchQuery research,
    IResearchRequestWriter requests,
    IClock clock,
    ILogger<ResearchCommandHandler> logger) : ICommandHandler
{
    private readonly ICompanyResearchQuery _research = research ?? throw new ArgumentNullException(nameof(research));
    private readonly IResearchRequestWriter _requests = requests ?? throw new ArgumentNullException(nameof(requests));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<ResearchCommandHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = request.Arguments?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return [Plain("Which company? Send /research followed by a name or domain.")];
        }

        var candidates = await _research.ResolveCandidatesAsync(query, cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            // Not in the registry — offer to add it rather than queueing a write for a company we cannot name.
            _logger.LogDebug("/research requested for an unknown company; queueing nothing.");
            return [Plain($"I don't have \"{query}\" in the registry yet. Add it and I'll research it.")];
        }

        if (candidates.Count > 1)
        {
            // A genuine ambiguity: name each match so the Owner can pick, never queue against the first silently.
            _logger.LogDebug("/research matched more than one company; queueing nothing and offering the choices.");
            var lines = candidates.Select(c => $"• {c.DisplayName}");
            return [Plain($"\"{query}\" matches more than one company:\n" + string.Join("\n", lines))];
        }

        var only = candidates[0];
        var dossier = only.LatestDossier;
        if (dossier is not null && !IsStale(dossier, _clock.UtcNow))
        {
            // Still fresh: make the needless refresh visible rather than paying for it (catalogue §Company).
            return [Plain(
                $"{only.DisplayName} already has a fresh dossier from {dossier.GeneratedAt:d MMM}. I'll refresh it once it ages.")];
        }

        // Absent or stale: queue for the next cycle. Idempotent per company per cycle, so a repeat is harmless.
        await _requests.EnqueueAsync(only.CompanyId, "on-demand /research command", cancellationToken).ConfigureAwait(false);

        var what = dossier is null ? "Researching" : "Refreshing the dossier for";
        return [Plain($"{what} {only.DisplayName}. The result arrives with tomorrow's digest.")];
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

    // A single plain line, escaped, so the Owner always gets a readable reply even for the error paths.
    private static RenderedMessage Plain(string text) =>
        RenderedMessage.PlainText(MarkdownV2Escaper.Escape(text));
}
