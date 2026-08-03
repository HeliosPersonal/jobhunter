namespace JobHunter.Domain.Jobs;

/// <summary>
/// How a job may be worked (data-model §jobs <c>remote_policy</c>). Persisted as <c>text</c>, never an
/// ordinal (coding-standards §5). <see cref="Unknown"/> is a first-class value, not a null: a provider
/// that does not say is recorded as not saying, never silently assumed on-site.
/// </summary>
public enum RemotePolicy
{
    /// <summary>Presence required at a location.</summary>
    Onsite,

    /// <summary>A mix of on-site and remote.</summary>
    Hybrid,

    /// <summary>Fully remote, no location constraint stated.</summary>
    Remote,

    /// <summary>Remote, but only within a stated region or set of countries.</summary>
    RemoteRegional,

    /// <summary>The provider did not state a policy.</summary>
    Unknown,
}
