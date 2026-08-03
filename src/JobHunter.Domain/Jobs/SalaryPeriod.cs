namespace JobHunter.Domain.Jobs;

/// <summary>
/// The period a salary figure is quoted over (data-model §jobs <c>salary_period</c>). Persisted as
/// <c>text</c>, never an ordinal (coding-standards §5). It is part of a <see cref="SalaryRange"/>'s
/// identity: two figures over different periods are not comparable without conversion, and
/// <see cref="SalaryRange"/> refuses to compare them.
/// </summary>
public enum SalaryPeriod
{
    /// <summary>Per year (annual salary).</summary>
    Year,

    /// <summary>Per month.</summary>
    Month,

    /// <summary>Per day (day rate).</summary>
    Day,

    /// <summary>Per hour (hourly rate).</summary>
    Hour,
}
