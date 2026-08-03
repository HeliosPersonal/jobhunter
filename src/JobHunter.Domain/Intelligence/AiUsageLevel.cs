namespace JobHunter.Domain.Intelligence;

/// <summary>
/// How much the engineering work involves building with or on AI systems — not what the company sells
/// (enrichment-schema §prompt). A company that markets an AI product but whose posting describes CRUD
/// work is <see cref="Low"/>. Persisted as <c>text</c>, never an ordinal (coding-standards §5).
/// </summary>
public enum AiUsageLevel
{
    /// <summary>No AI engineering content in the posting.</summary>
    None,

    /// <summary>Incidental AI tooling use.</summary>
    Low,

    /// <summary>AI is a meaningful part of the work.</summary>
    Medium,

    /// <summary>The role is substantially about building AI systems.</summary>
    High,

    /// <summary>
    /// Landing place for an unrecognised provider value (parsing step 8). Not part of the generated
    /// wire schema — the model is constrained to <see cref="None"/>..<see cref="High"/> — but the domain
    /// enum carries it so a provider adding a value degrades here rather than throwing at 03:00.
    /// </summary>
    Unknown,
}
