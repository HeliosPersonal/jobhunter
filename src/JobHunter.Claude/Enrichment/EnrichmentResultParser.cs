using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using EnrichmentAggregate = JobHunter.Domain.Intelligence.Enrichment;

namespace JobHunter.Claude.Enrichment;

/// <summary>
/// The Claude-side implementation of <see cref="IEnrichmentResultParser"/> (T12). It runs the raw tool-use
/// JSON through the eight-step <see cref="TolerantJsonParser"/> and, on success, maps the wire
/// <see cref="EnrichmentOutput"/> onto the <see cref="Enrichment"/> aggregate — turning the
/// <see cref="SalaryEstimateDto"/> into a <see cref="SalaryEstimate"/> value object via its own
/// fallible factory. It lives here, not in Application, so the whole tolerant-parsing contract stays in
/// the adapter and the Application handler depends only on the Domain port (architecture rule 3).
///
/// <para>A salary that fails the value-object's stricter validation is <em>dropped</em> while the rest of
/// the assessment is kept (parsing step 5) — the tolerant parser has already swapped, clamped or dropped
/// most malformations, so this is belt-and-braces for the residual case rather than a rejection. Parsing
/// never throws for a single item: a malformed result is one recorded failure, not a failed batch (QG-3).</para>
/// </summary>
public sealed class EnrichmentResultParser : IEnrichmentResultParser
{
    public EnrichmentParseOutcome Parse(EnrichmentParseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parsed = TolerantJsonParser.Parse(request.RawJson);
        if (!parsed.IsSuccess)
        {
            return EnrichmentParseOutcome.Failure(parsed.FailureReason!);
        }

        var output = parsed.Output!;
        var anomalies = new List<string>(parsed.Anomalies);
        var salary = ToSalaryEstimate(output.Salary, anomalies);

        // The construction guard re-asserts invariant 4; the tolerant parser has already guaranteed a
        // non-empty reasons list, so a throw here would be a genuine programmer error, not a bad item.
        var enrichment = new EnrichmentAggregate(
            request.EnrichmentId,
            request.JobId,
            request.RunId,
            salary,
            output.IsRemote,
            output.IsContractorFriendly,
            output.TimezoneBand,
            output.AiUsage,
            new AiSignals(
                output.AiSignals.BuildsAiProduct,
                output.AiSignals.BuildsAiInfra,
                output.AiSignals.UsesAiTooling,
                output.AiSignals.IsResearch),
            output.CompanyStage,
            output.RoleFamily,
            output.Technologies,
            output.Reasons,
            request.PromptVersion,
            request.CreatedAt);

        return EnrichmentParseOutcome.Success(enrichment, anomalies);
    }

    private static SalaryEstimate? ToSalaryEstimate(SalaryEstimateDto? dto, List<string> anomalies)
    {
        if (dto is null)
        {
            return null;
        }

        var result = SalaryEstimate.TryCreate(dto.Min, dto.Max, dto.Currency, dto.Period, dto.Confidence);
        if (result.IsSuccess)
        {
            return result.Value;
        }

        // The value object is stricter than the wire parser (e.g. a currency the parser allowed but the
        // value object rejects): drop the salary, keep the rest — silence on pay is better than a lie.
        anomalies.Add($"salary rejected by value object ({result.Error.Code}) → dropped");
        return null;
    }
}
