using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;

namespace JobHunter.Claude.Enrichment;

/// <summary>
/// The wire shape of one enrichment result, mirroring the output contract
/// (enrichment-schema §Output record). The C# record is the source of truth and the JSON Schema is
/// generated from it (<see cref="EnrichmentSchema"/>), so the prompt's declared output and the schema
/// the model is bound to cannot drift. Deserialisation is deliberately lenient — an unknown enum value
/// binds to <see cref="Domain.Intelligence.TimezoneBand.Unknown"/> et al via the tolerant parser, never
/// throwing (parsing step 8).
/// </summary>
public sealed record EnrichmentOutput(
    SalaryEstimateDto? Salary,
    bool IsRemote,
    bool IsContractorFriendly,
    TimezoneBand TimezoneBand,
    AiUsageLevel AiUsage,
    AiSignalsDto AiSignals,
    CompanyStage CompanyStage,
    RoleFamily RoleFamily,
    IReadOnlyList<string> Technologies,
    IReadOnlyList<string> Reasons);

/// <summary>
/// The wire shape of the AI sub-signals (enrichment-schema §Output record, TUNE-04). Each boolean is
/// derived from the described engineering work; an absent or non-boolean value degrades to <c>false</c>,
/// so the sub-signals are optional on the wire and never make an otherwise-valid item fail.
/// </summary>
public sealed record AiSignalsDto(
    bool BuildsAiProduct,
    bool BuildsAiInfra,
    bool UsesAiTooling,
    bool IsResearch);

/// <summary>The wire shape of an estimated salary; <c>null</c> on the parent is a legal "cannot tell".</summary>
public sealed record SalaryEstimateDto(
    decimal Min,
    decimal Max,
    string Currency,
    SalaryPeriod Period,
    decimal Confidence);
