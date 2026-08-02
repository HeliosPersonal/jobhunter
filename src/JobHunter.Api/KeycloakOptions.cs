namespace JobHunter.Api;

/// <summary>
/// Keycloak OIDC settings for the API (ADR-0014). The API validates a JWT bearer against the helios
/// realm and requires the <c>sub</c> claim to match the configured Owner subject — a valid token for a
/// different subject is a 403, not a 200 (single Owner, invariant 9).
/// </summary>
public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>The OIDC authority, e.g. <c>https://keycloak.helios/realms/jobhunter</c>.</summary>
    public string Authority { get; init; } = string.Empty;

    /// <summary>The expected token audience.</summary>
    public string Audience { get; init; } = "jobhunter-api";

    /// <summary>The Owner's <c>sub</c>. A token for any other subject is rejected.</summary>
    public string OwnerSubject { get; init; } = string.Empty;

    /// <summary>Require HTTPS metadata retrieval. False only for local Keycloak over http.</summary>
    public bool RequireHttpsMetadata { get; init; } = true;

    /// <summary>True when an authority is configured and bearer validation should be wired.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Authority);
}
