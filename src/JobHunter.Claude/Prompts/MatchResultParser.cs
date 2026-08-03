using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;

namespace JobHunter.Claude.Prompts;

/// <summary>
/// The Claude-side implementation of <see cref="IMatchResultParser"/> (T04, wired in T06). It runs the raw
/// tool-use JSON through the <see cref="TolerantMatchParser"/> and, on success, maps the wire
/// <see cref="MatchOutput"/> onto the <see cref="Match"/> aggregate — turning the
/// <see cref="SalaryExpectationDto"/> into a <see cref="SalaryExpectation"/> value object via its own
/// fallible factory. It lives here, not in Application, so the whole tolerant-parsing contract stays in the
/// adapter and the Application handler depends only on the Domain port (architecture rule 3).
///
/// <para>A salary that fails the value object's stricter validation is <em>dropped</em> while the rest of
/// the match is kept — the tolerant parser has already swapped or dropped most malformations, so this is
/// belt-and-braces for the residual case rather than a rejection. Parsing never throws for a single item:
/// a malformed result is one recorded failure, not a failed batch (QG-3).</para>
///
/// <para>No CV text reaches this type: it maps the model's <em>output</em> onto the aggregate, and neither
/// the raw JSON nor the resulting <see cref="Match"/> carries CV content.</para>
/// </summary>
public sealed class MatchResultParser : IMatchResultParser
{
    public MatchParseOutcome Parse(MatchParseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parsed = TolerantMatchParser.Parse(request.RawJson);
        if (!parsed.IsSuccess)
        {
            return MatchParseOutcome.Failure(parsed.FailureReason!);
        }

        var output = parsed.Output!;
        var anomalies = new List<string>(parsed.Anomalies);
        var salary = ToSalaryExpectation(output.SalaryExpectation, anomalies);

        // The construction guard re-asserts invariant 4; the tolerant parser has already guaranteed a
        // non-empty reasons list, so a throw here would be a genuine programmer error, not a bad item.
        var match = new Match(
            request.MatchId,
            request.JobId,
            request.RunId,
            request.ProfileId,
            request.CvVersionId,
            output.MatchScore,
            output.InterviewProbability,
            output.MissingSkills,
            salary,
            output.Reasons,
            request.PromptVersion,
            request.CreatedAt);

        return MatchParseOutcome.Success(match, anomalies);
    }

    private static SalaryExpectation? ToSalaryExpectation(SalaryExpectationDto? dto, List<string> anomalies)
    {
        if (dto is null)
        {
            return null;
        }

        var result = SalaryExpectation.TryCreate(dto.Min, dto.Max, dto.Currency);
        if (result.IsSuccess)
        {
            return result.Value;
        }

        // The value object is stricter than the wire parser (e.g. a currency the parser allowed but the
        // value object rejects): drop the salary, keep the rest — silence on pay is better than a lie.
        anomalies.Add($"salaryExpectation rejected by value object ({result.Error.Code}) → dropped");
        return null;
    }
}
