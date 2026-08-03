using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Normalization;

/// <summary>
/// Decides a job's <see cref="RemotePolicy"/> (T03, SAD §8). The rule is strict about provenance: an
/// explicit provider signal always wins (Lever's workplace type, Ashby's remote boolean); only in its
/// absence is the policy inferred from the location <em>text</em>; it is <strong>never</strong> guessed
/// from the description, which is too variable to trust. When nothing says, the answer is
/// <see cref="RemotePolicy.Unknown"/> — a first-class value, never silently assumed on-site.
/// </summary>
public static class RemotePolicyResolver
{
    /// <summary>
    /// Resolves the policy. If <paramref name="explicitSignal"/> is non-null it is returned unchanged —
    /// the provider is authoritative. Otherwise <paramref name="locationText"/> is inspected for remote/
    /// hybrid wording, and a regional qualifier ("Remote - EMEA") yields <see cref="RemotePolicy.RemoteRegional"/>.
    /// </summary>
    public static RemotePolicy Resolve(RemotePolicy? explicitSignal, string? locationText)
    {
        if (explicitSignal is not null)
        {
            return explicitSignal.Value;
        }

        if (string.IsNullOrWhiteSpace(locationText))
        {
            return RemotePolicy.Unknown;
        }

        var text = locationText.ToLowerInvariant();

        if (Contains(text, "hybrid"))
        {
            return RemotePolicy.Hybrid;
        }

        if (Contains(text, "remote") || Contains(text, "anywhere") || Contains(text, "work from home"))
        {
            return HasRegionalQualifier(text) ? RemotePolicy.RemoteRegional : RemotePolicy.Remote;
        }

        // Named a place and said nothing about remote — treat as on-site.
        return RemotePolicy.Onsite;
    }

    private static bool HasRegionalQualifier(string text)
    {
        // "Remote" with a region or country attached is regional, not global. "Anywhere"/"worldwide"
        // explicitly deny a qualifier and stay global even if other words are present.
        if (Contains(text, "anywhere") || Contains(text, "worldwide") || Contains(text, "global"))
        {
            return false;
        }

        // A separator or a region keyword after the remote word signals a scoped remote.
        string[] qualifiers =
        [
            "emea", "apac", "amer", "us", "usa", "uk", "eu", "europe", "america", "canada",
            "germany", "-", "(", ",", "within", "based",
        ];

        return qualifiers.Any(q => Contains(text, q));
    }

    private static bool Contains(string text, string token) =>
        text.Contains(token, StringComparison.Ordinal);
}
