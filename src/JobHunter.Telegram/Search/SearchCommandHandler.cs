using JobHunter.Domain.Abstractions;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Search;

/// <summary>
/// The <c>/search &lt;query&gt;</c> command (F9-T09): parse the inline query, run it through the shared
/// <see cref="ISearchQuery"/> port — the same query service the API uses, only the renderer differs (the
/// O12 decision, so there is no second search path) — and render the results in the digest card layout.
///
/// <para>Every outcome is a rendered message, never an exception into the bot loop: an unreachable index
/// (an <see cref="ISearchQuery"/> failure, QG-3) produces a clear "search is unavailable" line and is
/// logged, so a Typesense outage degrades <c>/search</c> alone and leaves every other command working
/// (DoD). No CV-derived value can appear — the results carry only the allowlisted
/// <see cref="Domain.Search.JobDocument"/> (QG-2).</para>
/// </summary>
internal sealed class SearchCommandHandler(ISearchQuery search, IClock clock, ILogger<SearchCommandHandler> logger)
{
    private readonly ISearchQuery _search = search ?? throw new ArgumentNullException(nameof(search));

    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    private readonly ILogger<SearchCommandHandler> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<string> HandleAsync(string? arguments, CancellationToken cancellationToken = default)
    {
        // The clock resolves the catalogue's relative `since:30d` window to an absolute cutoff (IClock is the
        // one time source — architecture rule 5 bans DateTime.UtcNow outside SystemClock).
        var query = SearchCommandParser.Parse(arguments, _clock.UtcNow);
        var result = await _search.SearchAsync(query, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "The /search command could not reach the index: {Error}.", result.Error.Code);
            return "_" + MarkdownV2Escaper.Escape(
                "Search is unavailable right now. Please try again in a moment.") + "_";
        }

        return SearchResultRenderer.Render(arguments ?? string.Empty, result.Value);
    }
}
