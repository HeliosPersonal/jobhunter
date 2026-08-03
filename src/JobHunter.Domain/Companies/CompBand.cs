namespace JobHunter.Domain.Companies;

/// <summary>
/// A coarse compensation band for an employer's typical senior/staff engineering posture (T15, TUNE-10).
/// It is a category label, not money — no amount, no currency — so it persists as <c>text</c> and biases
/// acquisition and digest ordering toward the Owner's target band. It is advisory: a lower band never
/// excludes a company, it only orders it later (reason-visible, not a silent filter). A company with no
/// band is untagged, ordered after every tagged one.
/// </summary>
public enum CompBand
{
    /// <summary>Top-of-market comp (e.g. frontier AI labs, top US scale-ups).</summary>
    Top,

    /// <summary>Strong comp, typically well-funded US-headquartered firms.</summary>
    High,

    /// <summary>Mid-band comp, typically smaller or non-US employers.</summary>
    Mid,
}
