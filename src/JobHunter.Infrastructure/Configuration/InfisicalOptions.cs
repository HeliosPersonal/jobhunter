namespace JobHunter.Infrastructure.Configuration;

/// <summary>
/// Machine-identity credentials for the Infisical runtime secret fetch (ADR-0011). Only the identity
/// lives in a k8s Secret; the actual application secrets are fetched at startup and never enter the
/// repository or the image layers (invariant 12). Absent in Development, where the fetch is skipped.
/// </summary>
public sealed class InfisicalOptions
{
    public const string SectionName = "Infisical";

    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }

    public string? ProjectId { get; init; }

    public string? SiteUrl { get; init; }

    public string Environment { get; init; } = "prod";

    /// <summary>True when all three identity fields are present, i.e. a fetch can be attempted.</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(ProjectId);
}
