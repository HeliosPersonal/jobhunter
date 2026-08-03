namespace JobHunter.Domain.Jobs;

/// <summary>
/// The seniority level extracted from a job's title (data-model §jobs <c>seniority</c>). Persisted as
/// <c>text</c>, never an ordinal (coding-standards §5). The column is nullable, so absence is modelled
/// as a <c>Seniority?</c> — a title that carries no recognisable level leaves it null rather than
/// guessing.
/// </summary>
public enum Seniority
{
    /// <summary>Entry level: "Junior", "Jr.", "Graduate", "Associate".</summary>
    Junior,

    /// <summary>The unmarked middle: "Mid", "Intermediate", or no marker at all when one is implied.</summary>
    Mid,

    /// <summary>"Senior", "Sr.".</summary>
    Senior,

    /// <summary>"Staff".</summary>
    Staff,

    /// <summary>"Principal".</summary>
    Principal,

    /// <summary>"Lead", "Tech Lead".</summary>
    Lead,

    /// <summary>"Manager", "Engineering Manager".</summary>
    Manager,
}
