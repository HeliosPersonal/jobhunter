using JobHunter.Application.Abstractions;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Research;
using JobHunter.Scrapers.Parsing;
using Microsoft.Extensions.Logging;

namespace JobHunter.Scrapers.Research;

/// <summary>
/// The shared shape of the company-scoped page fetchers (SAD §5): a category whose evidence lives on the
/// company's own site probes a fixed list of paths on the company's canonical domain, each through the
/// guarded fetch path (<see cref="IGuardedResearchFetch"/>, SAD §5 S5), and keeps the ones that yield
/// extractable text. A subclass supplies only its <see cref="Category"/> and its <see cref="Paths"/>.
///
/// <para>Every non-success outcome is <em>no document</em>, never an exception (AC-07): a refusal (scheme,
/// allowlist, SSRF — the guard's decision, not ours), a non-2xx status, an empty or script-only page, or
/// even a genuinely dead candidate that throws mid-fetch, all reduce to skipping that candidate. So one
/// dead path never suppresses the others and a dead category never brings down the dossier (SAD §4 S4).
/// The company domain is passed to the guard so its company-scoped allowlist can bind the target to this
/// company's own site.</para>
/// </summary>
internal abstract class CompanyPageFetcher(
    IGuardedResearchFetch fetch,
    IClock clock,
    ILogger logger) : IResearchFetcher
{
    private readonly IGuardedResearchFetch _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public abstract ResearchCategory Category { get; }

    /// <summary>The absolute paths, in priority order, this category probes on the company's own domain.</summary>
    protected abstract IReadOnlyList<string> Paths { get; }

    public async Task<IReadOnlyList<FetchedDocument>> FetchAsync(
        Company company,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(company);

        var domain = company.CanonicalDomain.Value;
        var documents = new List<FetchedDocument>();

        foreach (var path in Paths)
        {
            var url = new Uri($"https://{domain}{path}");
            var document = await TryFetchAsync(url, domain, cancellationToken).ConfigureAwait(false);
            if (document is not null)
            {
                documents.Add(document);
            }
        }

        return documents;
    }

    private async Task<FetchedDocument?> TryFetchAsync(Uri url, string domain, CancellationToken cancellationToken)
    {
        ResearchFetchResult result;
        try
        {
            result = await _fetch.FetchAsync(Category, url, domain, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            // One dead candidate (a connection failure, or the guard's own SSRF refusal surfacing as a
            // socket error on a rebinding host) is isolated to this path — the category degrades, not the run.
            _logger.LogWarning(exception, "Research fetch for {Category} failed at {Url}", Category, url);
            return null;
        }

        if (result.Outcome is not ResearchFetchOutcome.Ok || result.Body is null || result.FinalUrl is null)
        {
            _logger.LogInformation(
                "Research fetch for {Category} at {Url} yielded no document ({Outcome} {Reason})",
                Category, url, result.Outcome, result.Reason);
            return null;
        }

        var text = ResearchContentExtractor.ToPlainText(result.Body);
        if (text.Length == 0)
        {
            // A page with no extractable text is no document — there is no headless browser (T04 "Done when").
            return null;
        }

        var title = PageTitle.From(result.Body);
        return new FetchedDocument(result.FinalUrl.ToString(), title, text, _clock.UtcNow);
    }
}
