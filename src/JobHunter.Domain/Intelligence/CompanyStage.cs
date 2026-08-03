namespace JobHunter.Domain.Intelligence;

/// <summary>
/// The company's funding/maturity stage, only ever inferred from evidence in the posting
/// (enrichment-schema §prompt). Fed back onto <c>companies.stage</c> (data-model §enrichments).
/// Persisted as <c>text</c>, never an ordinal (coding-standards §5). <see cref="Unknown"/> is the
/// default when the posting gives no evidence.
/// </summary>
public enum CompanyStage
{
    /// <summary>Seed stage.</summary>
    Seed,

    /// <summary>Series A.</summary>
    SeriesA,

    /// <summary>Series B.</summary>
    SeriesB,

    /// <summary>Series C.</summary>
    SeriesC,

    /// <summary>Series D or later private round.</summary>
    SeriesD,

    /// <summary>Publicly traded.</summary>
    Public,

    /// <summary>Profitable and self-funded, no venture round mentioned.</summary>
    Bootstrapped,

    /// <summary>The posting gives no evidence of stage — the default, never a guess.</summary>
    Unknown,
}
