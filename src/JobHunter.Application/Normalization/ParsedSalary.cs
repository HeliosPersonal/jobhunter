using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Normalization;

/// <summary>
/// The outcome of parsing a salary string (T03). <see cref="Range"/> is the structured figure when one
/// could be parsed; <see cref="Raw"/> is the original text, retained whenever it is present so nothing is
/// lost to a parser gap (data-model §jobs <c>salary_raw</c>). An unparseable input ("Competitive", an
/// unknown currency) yields a null <see cref="Range"/> and a populated <see cref="Raw"/> — never a zero
/// and never a null-coerced range.
/// </summary>
public sealed record ParsedSalary(SalaryRange? Range, string? Raw)
{
    /// <summary>Nothing to record: the provider supplied no salary text at all.</summary>
    public static readonly ParsedSalary None = new(null, null);

    public bool HasStructuredRange => Range is not null;
}
