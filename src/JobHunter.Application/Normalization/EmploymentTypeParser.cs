using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Normalization;

/// <summary>
/// Maps a provider's employment-type string to the canonical <see cref="EmploymentType"/> (data-model
/// §jobs). Providers spell the same engagement differently — <c>Full-time</c>, <c>FULL_TIME</c>,
/// <c>FullTime</c> — so the token is lower-cased invariantly and stripped of separators before matching.
/// An unrecognised or absent value is <see cref="EmploymentType.Unknown"/>, never silently full-time.
/// Pure: no clock, no randomness, invariant comparison only.
/// </summary>
public static class EmploymentTypeParser
{
    public static EmploymentType Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return EmploymentType.Unknown;
        }

        var token = value.ToLowerInvariant()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        return token switch
        {
            "fulltime" => EmploymentType.FullTime,
            "parttime" => EmploymentType.PartTime,
            "contract" or "contractor" or "temporary" or "freelance" => EmploymentType.Contract,
            "internship" or "intern" => EmploymentType.Internship,
            _ => EmploymentType.Unknown,
        };
    }
}
