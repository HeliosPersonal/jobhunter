namespace JobHunter.Application.Normalization;

/// <summary>
/// The Owner's target-title tiers (T11, review §5). Tier-1 titles are the bullseye, Tier-2 the strong
/// adjacents, and Tier-3 the titles that only qualify once the <em>work described</em> is judged — the
/// distinction the downstream F3 <c>RoleFamily</c> classifier acts on. The tier is a reference band, not
/// a score: nothing here ranks a job. It exists so the classifier and the F4 Profile goal fields are
/// testable and reviewable in a diff.
/// </summary>
public enum TitleTier
{
    /// <summary>Bullseye target titles.</summary>
    Tier1,

    /// <summary>Strong adjacent titles.</summary>
    Tier2,

    /// <summary>Titles that qualify only once the work described is judged.</summary>
    Tier3,
}

/// <summary>
/// The canonical role-family archetype vocabulary (TUNE-03). The F3 <c>RoleFamily</c> enum (F3 T15,
/// enrichment-schema §RoleFamily) is the future home of this vocabulary; until F3 lands it is encoded once
/// here so the <c>title-tiers.yaml</c> reference config can be validated against it, and so the two can be
/// asserted never to drift apart. Every archetype a title tier maps to must be one of these names.
/// </summary>
public static class RoleFamilyArchetypes
{
    /// <summary>
    /// The role-family archetype names, matching the documented F3 <c>RoleFamily</c> enum. Ordinal set so
    /// membership is culture-invariant, like the technology matcher (SAD S5).
    /// </summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "AiPlatform",
        "Platform",
        "AiApplications",
        "ForwardDeployed",
        "FoundingEng",
        "BackendGeneric",
        "Frontend",
        "Fullstack",
        "DevOpsSRE",
        "MlResearch",
        "DataScience",
        "PromptEng",
        "EnterpriseCrud",
        "Other",
    };
}

/// <summary>
/// One target title mapped onto its tier and role-family archetype. A pure input record for
/// <see cref="TitleTierConfig"/>; the archetype is validated against <see cref="RoleFamilyArchetypes"/> on
/// construction of the config, not here.
/// </summary>
public sealed record TitleTierEntry(TitleTier Tier, string Title, string RoleFamily);

/// <summary>
/// The validated title-tier reference config (T11). It maps each Owner-target title onto a
/// <see cref="TitleTier"/> and a <see cref="RoleFamilyArchetypes"/> name. Construction validates that every
/// title is non-blank and unique and that every role-family archetype is a known one — an unknown archetype
/// or a duplicated title is a load-time operator error, rejected here rather than drifting from the F3
/// classifier at run time. No scoring logic lives here: it is a committed, reviewable reference.
/// </summary>
public sealed class TitleTierConfig
{
    private readonly List<TitleTierEntry> _entries;

    /// <summary>
    /// Builds the config from <paramref name="entries"/>. A blank title, a title repeated (case-insensitively)
    /// across the config, or a role-family archetype outside <see cref="RoleFamilyArchetypes.All"/> is a
    /// construction failure — the reference would be ambiguous or would drift from the F3 vocabulary.
    /// </summary>
    public TitleTierConfig(IEnumerable<TitleTierEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _entries = [];
        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);

            if (string.IsNullOrWhiteSpace(entry.Title))
            {
                throw new ArgumentException("A title-tier entry has a blank title.", nameof(entries));
            }

            var title = entry.Title.Trim();
            if (!seenTitles.Add(title))
            {
                throw new ArgumentException(
                    $"The title-tier config repeats the title '{title}'.", nameof(entries));
            }

            if (!RoleFamilyArchetypes.All.Contains(entry.RoleFamily))
            {
                throw new ArgumentException(
                    $"The title '{title}' maps to unknown role family '{entry.RoleFamily}'.", nameof(entries));
            }

            _entries.Add(entry with { Title = title });
        }
    }

    /// <summary>The number of target titles in the config.</summary>
    public int Count => _entries.Count;

    /// <summary>The tier entries, in file order.</summary>
    public IReadOnlyList<TitleTierEntry> Entries => _entries;

    /// <summary>The distinct role-family archetypes the config maps titles onto.</summary>
    public IReadOnlySet<string> RoleFamilies =>
        _entries.Select(e => e.RoleFamily).ToHashSet(StringComparer.Ordinal);
}
