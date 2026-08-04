using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;

namespace JobHunter.Claude.Prompts;

/// <summary>
/// The wire shape of one match result, mirroring the output contract (match-schema §Output record). The
/// C# record is the source of truth and the JSON Schema is generated from it (<see cref="MatchSchema"/>),
/// so the prompt's declared output and the schema the model is bound to cannot drift. Deserialisation is
/// deliberately lenient — an unrecognised interview-probability band binds to
/// <see cref="Domain.Intelligence.InterviewProbability.Low"/> via the tolerant parser, never throwing.
///
/// <para>This is the model's <em>output</em> only. It carries a score, a band, the missing skills, an
/// optional salary expectation and the reasons — <strong>no CV text</strong>, which was materialised once
/// in the prompt and never travels with the result.</para>
/// </summary>
public sealed record MatchOutput(
    int MatchScore,
    InterviewProbability InterviewProbability,
    IReadOnlyList<string> MissingSkills,
    SalaryExpectationDto? SalaryExpectation,
    IReadOnlyList<string> Reasons);

/// <summary>
/// The wire shape of a salary expectation — what the Owner could plausibly ask for <em>this</em> role;
/// <c>null</c> on the parent is a legal "the posting gives nothing to anchor on" (match-schema §Output).
/// </summary>
public sealed record SalaryExpectationDto(
    decimal Min,
    decimal Max,
    string Currency,
    SalaryPeriod Period);
