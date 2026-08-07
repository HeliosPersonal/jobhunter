using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Common;
using JobHunter.Domain.Intelligence;

namespace JobHunter.Domain.Companies;

/// <summary>
/// The identity of a hiring organisation, keyed by its <see cref="CanonicalDomain"/> so a rebrand, an
/// ATS migration or a name change never orphans its jobs (data-model §companies). Discovery never
/// creates a company — it only binds an existing one (invariant: detection binds, it does not invent).
/// A company is only fetched once it is <see cref="IsActive"/> <em>and</em> has a confident binding;
/// <see cref="ActivateForDiscovery"/> enforces the confidence half of that rule (AC-04).
/// </summary>
public sealed class Company : Entity
{
    public static readonly Error BlankDisplayName =
        new("company.display_name.blank", "A company requires a non-blank display name.");

    public static readonly Error NoConfidentBinding =
        new(
            "company.activation.no_confident_binding",
            "A company cannot be activated for discovery without a live binding of confidence ≥ 0.80.");

    public Company(
        Guid id,
        CanonicalDomain canonicalDomain,
        string displayName,
        CompanySource source,
        DateTimeOffset firstSeenAt,
        string? careersUrl = null,
        string? hqCountry = null,
        bool isActive = true,
        CompBand? compBand = null,
        bool? remoteEmeaFriendly = null)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(canonicalDomain);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        CanonicalDomain = canonicalDomain;
        DisplayName = displayName;
        Source = source;
        CareersUrl = careersUrl;
        HqCountry = hqCountry;
        IsActive = isActive;
        CompBand = compBand;
        RemoteEmeaFriendly = remoteEmeaFriendly;
        FirstSeenAt = firstSeenAt;
        LastSeenAt = firstSeenAt;
    }

    private Company()
    {
        CanonicalDomain = null!;
        DisplayName = string.Empty;
    }

    public CanonicalDomain CanonicalDomain { get; private set; }

    public string DisplayName { get; private set; }

    /// <summary>A detection hint, not authoritative (data-model §companies).</summary>
    public string? CareersUrl { get; private set; }

    /// <summary>ISO-3166-1 alpha-2, or null when unknown.</summary>
    public string? HqCountry { get; private set; }

    /// <summary>Set by F3, not at discovery.</summary>
    public string? Stage { get; private set; }

    /// <summary>Set by F8, not at discovery.</summary>
    public string? EmployeeBand { get; private set; }

    /// <summary>
    /// The instant of the research observation behind the current <see cref="Stage"/> / <see cref="EmployeeBand"/>,
    /// null until F8 first feeds firmographics back. It arbitrates a disagreement: a later observation wins,
    /// an older or equal one is ignored (AC-10).
    /// </summary>
    public DateTimeOffset? FirmographicsObservedAt { get; private set; }

    /// <summary>
    /// The employer's coarse comp posture (T15), from the curated seed. Advisory: it biases discovery and
    /// digest ordering toward the target band, never a hard filter. Null when the row is untagged.
    /// </summary>
    public CompBand? CompBand { get; private set; }

    /// <summary>
    /// Whether the employer hires remote from EMEA / Ukraine (T15), from the curated seed. Advisory: it
    /// feeds acquisition prioritisation with a reason, never a silent exclusion. Null when untagged.
    /// </summary>
    public bool? RemoteEmeaFriendly { get; private set; }

    /// <summary>Where the registry entry came from.</summary>
    public CompanySource Source { get; private set; }

    /// <summary>When false, the company is excluded from the discovery fan-out.</summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset FirstSeenAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>
    /// Creates a company, or a failure if the display name is blank. Preferred over the constructor on
    /// the seeding/crawl path where a malformed row is an expected, recorded outcome rather than a bug.
    /// </summary>
    public static Result<Company> TryCreate(
        Guid id,
        CanonicalDomain canonicalDomain,
        string displayName,
        CompanySource source,
        DateTimeOffset firstSeenAt,
        string? careersUrl = null,
        string? hqCountry = null,
        bool isActive = true)
    {
        ArgumentNullException.ThrowIfNull(canonicalDomain);

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return BlankDisplayName;
        }

        return Result<Company>.Success(
            new Company(id, canonicalDomain, displayName, source, firstSeenAt, careersUrl, hqCountry, isActive));
    }

    /// <summary>
    /// Activates the company for discovery, but only when at least one supplied binding is live and
    /// meets the discovery threshold (≥ 0.80). Attributing another company's jobs is far worse than
    /// missing a company, so activation without a confident binding is refused (AC-04). Idempotent when
    /// already active with a confident binding.
    /// </summary>
    public Result<Company> ActivateForDiscovery(IEnumerable<AtsBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        if (!bindings.Any(b => b.IsLive && b.Confidence.IsConfident))
        {
            return NoConfidentBinding;
        }

        IsActive = true;
        return Result<Company>.Success(this);
    }

    /// <summary>
    /// Records that the company was seen again at the clock's instant, keeping the registry's liveness
    /// current. Never moves <see cref="LastSeenAt"/> backwards.
    /// </summary>
    public void Touch(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.UtcNow;
        if (now > LastSeenAt)
        {
            LastSeenAt = now;
        }
    }

    /// <summary>
    /// Excludes the company from discovery. Idempotent — retiring an already-inactive company is a
    /// no-op (the registry converges regardless of how many times a crawl reports the same closure).
    /// </summary>
    public void Retire() => IsActive = false;

    /// <summary>
    /// Feeds firmographics learned from research back onto the registry (AC-10) — the whitelisted
    /// cross-owner write in F8. The observation only lands when it is strictly newer than the one already
    /// recorded, so a re-run of an older dossier never overwrites a fresher classification and a
    /// disagreement resolves to the newer observation. A field the model left null (no evidence) is not
    /// applied, and both-null is a no-op. Returns whether anything changed.
    /// </summary>
    public bool ApplyFirmographics(CompanyStage? stage, string? employeeBand, DateTimeOffset observedAt)
    {
        if (stage is null && employeeBand is null)
        {
            return false;
        }

        if (FirmographicsObservedAt is { } current && observedAt <= current)
        {
            return false;
        }

        if (stage is { } s)
        {
            Stage = s.ToString();
        }

        if (employeeBand is not null)
        {
            EmployeeBand = employeeBand;
        }

        FirmographicsObservedAt = observedAt;
        return true;
    }
}
