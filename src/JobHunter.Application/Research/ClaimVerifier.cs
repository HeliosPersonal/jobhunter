using JobHunter.Domain.Research;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Research;

/// <summary>
/// The rule the whole feature rests on (research-schema §Citation verification, QG-1, invariant 5): a
/// synthesised claim is kept only if its cited URL is a member of the set of URLs actually fetched
/// <em>for this dossier</em>. Membership is exact after normalising scheme, host case and a single trailing
/// slash — and nothing else. This is deliberately <strong>not</strong> fuzzy: a claim citing a URL "close
/// to" a real one is precisely the failure mode being guarded against, so a different path, an injected
/// query parameter, or a URL borrowed from another company's research all fail the check and are discarded.
///
/// <para>Every discarded claim is counted on the result and its fabricated URL is logged at
/// <see cref="LogLevel.Warning"/> (AC-08) — the model's invention is the interesting signal. The verifier is
/// pure of clock and identity: a matched claim inherits its observed date from the source it cites, and the
/// orchestrator (T08) mints the <see cref="ResearchClaim"/> id, so this stays a deterministic function of its
/// inputs and its log.</para>
/// </summary>
public sealed class ClaimVerifier(ILogger<ClaimVerifier> logger)
{
    private readonly ILogger<ClaimVerifier> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Partitions <paramref name="claims"/> into the ones whose cited URL matches a fetched
    /// <paramref name="sources"/> URL (kept, paired to that source) and the ones that do not (discarded and
    /// counted). Sources sharing a normalised URL resolve to the first supplied — a dossier does not fetch
    /// the same URL under two categories.
    /// </summary>
    public ClaimVerification Verify(
        IReadOnlyList<ResearchSource> sources,
        IReadOnlyList<UnverifiedClaim> claims)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(claims);

        var fetched = new Dictionary<string, ResearchSource>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            var key = Normalise(source.Url);
            if (key is not null)
            {
                fetched.TryAdd(key, source);
            }
        }

        var verified = new List<VerifiedClaim>();
        var discarded = 0;

        foreach (var claim in claims)
        {
            var key = Normalise(claim.SourceUrl);
            if (key is not null && fetched.TryGetValue(key, out var source))
            {
                verified.Add(new VerifiedClaim(source, claim.Category, claim.Claim, claim.IsWarning));
            }
            else
            {
                discarded++;
                // The fabricated URL is the signal worth keeping — never the claim text (it may be plausible).
                _logger.LogWarning(
                    "Discarded research claim citing an unfetched URL {FabricatedUrl}.", claim.SourceUrl);
            }
        }

        return new ClaimVerification(verified, discarded);
    }

    /// <summary>
    /// The one permitted tolerance: lowercase the scheme and host, drop a single trailing slash on the path,
    /// and require the URL to be an absolute HTTP(S) URI with no query and no fragment. Anything the model
    /// added — a query parameter, a fragment, a different path — changes the key and so fails membership. An
    /// unparseable URL normalises to <see langword="null"/>, which can never match a source.
    /// </summary>
    private static string? Normalise(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        // A query or fragment is exactly the "close to" tampering the check must reject, so its presence
        // is preserved in the key and can never match a stored source URL (which carries neither).
        var path = uri.AbsolutePath.Length > 1
            ? uri.AbsolutePath.TrimEnd('/')
            : uri.AbsolutePath;

        return string.Concat(
            uri.Scheme,
            "://",
            uri.Host,
            path,
            uri.Query,
            uri.Fragment);
    }
}
