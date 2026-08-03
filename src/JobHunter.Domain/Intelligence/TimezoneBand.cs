namespace JobHunter.Domain.Intelligence;

/// <summary>
/// Where a role expects working-hours overlap — often not where the company is (enrichment-schema
/// §prompt). Persisted as <c>text</c>, never an ordinal (coding-standards §5). <see cref="Unknown"/>
/// is the landing place for an unrecognised or absent provider value, so step 8 of parsing can degrade
/// rather than throw.
/// </summary>
public enum TimezoneBand
{
    /// <summary>Europe, Middle East and Africa overlap.</summary>
    EMEA,

    /// <summary>The Americas overlap.</summary>
    AMER,

    /// <summary>Asia-Pacific overlap.</summary>
    APAC,

    /// <summary>No particular overlap is required.</summary>
    Global,

    /// <summary>The posting gives no evidence of an expected overlap.</summary>
    Unknown,
}
