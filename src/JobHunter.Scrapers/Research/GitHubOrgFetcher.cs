using System.Globalization;
using System.Text;
using System.Text.Json;
using JobHunter.Application.Abstractions;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Research;
using Microsoft.Extensions.Logging;

namespace JobHunter.Scrapers.Research;

/// <summary>
/// The open-source fetcher (SAD §5, contract §Fetcher set): the one third-party company-research source
/// with a public, auth-free API. It derives the organisation login from the company's registrable domain
/// (<c>acme.com</c> → <c>acme</c>), queries the org's public repositories through the guarded fetch path —
/// host <c>api.github.com</c>, which the OpenSource allowlist permits — and folds the repository list into
/// a single document describing the public presence. Forks are excluded, so a wall of forked repos never
/// inflates the picture. Every failure (no such org, an error status, an empty or malformed body) is no
/// document, never an exception (AC-07).
/// </summary>
internal sealed class GitHubOrgFetcher(
    IGuardedResearchFetch fetch,
    IClock clock,
    ILogger<GitHubOrgFetcher> logger) : IResearchFetcher
{
    private readonly IGuardedResearchFetch _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<GitHubOrgFetcher> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public ResearchCategory Category => ResearchCategory.OpenSource;

    public async Task<IReadOnlyList<FetchedDocument>> FetchAsync(
        Company company,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(company);

        var domain = company.CanonicalDomain.Value;
        var org = OrgLogin(domain);
        // Public repos, newest activity first, one page — enough signal for a summary without paging the world.
        var url = new Uri($"https://api.github.com/orgs/{Uri.EscapeDataString(org)}/repos?per_page=100&sort=pushed");

        ResearchFetchResult result;
        try
        {
            result = await _fetch.FetchAsync(Category, url, domain, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "GitHub org fetch failed for {Org}", org);
            return [];
        }

        if (result.Outcome is not ResearchFetchOutcome.Ok || result.Body is null || result.FinalUrl is null)
        {
            _logger.LogInformation(
                "GitHub org fetch for {Org} yielded no document ({Outcome} {Reason})",
                org, result.Outcome, result.Reason);
            return [];
        }

        var text = Summarise(result.Body);
        if (text.Length == 0)
        {
            return [];
        }

        return [new FetchedDocument(result.FinalUrl.ToString(), $"{company.DisplayName} on GitHub", text, _clock.UtcNow)];
    }

    // The org login is the registrable domain's leading label — acme.com → acme, big-corp.io → big-corp.
    private static string OrgLogin(string domain)
    {
        var dot = domain.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 ? domain[..dot] : domain;
    }

    // Fold the repo array into one plain-text block: one line per non-fork repo with its language, stars and
    // description. A malformed body is caught as no text, not a throw (AC-07).
    private static string Summarise(string body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return string.Empty;
        }

        using (document)
        {
            if (document.RootElement.ValueKind is not JsonValueKind.Array)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var repo in document.RootElement.EnumerateArray())
            {
                if (repo.ValueKind is not JsonValueKind.Object)
                {
                    continue;
                }

                if (repo.TryGetProperty("fork", out var fork)
                    && fork.ValueKind is JsonValueKind.True)
                {
                    continue;
                }

                AppendRepo(builder, repo);
            }

            return builder.ToString().TrimEnd();
        }
    }

    private static void AppendRepo(StringBuilder builder, JsonElement repo)
    {
        var name = ReadString(repo, "name");
        if (name.Length == 0)
        {
            return;
        }

        builder.Append(name);

        var language = ReadString(repo, "language");
        if (language.Length > 0)
        {
            builder.Append(" [").Append(language).Append(']');
        }

        if (repo.TryGetProperty("stargazers_count", out var stars)
            && stars.ValueKind is JsonValueKind.Number
            && stars.TryGetInt32(out var count))
        {
            builder.Append(' ').Append(count.ToString(CultureInfo.InvariantCulture)).Append(" stars");
        }

        var description = ReadString(repo, "description");
        if (description.Length > 0)
        {
            builder.Append(" — ").Append(description);
        }

        builder.Append('\n');
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
