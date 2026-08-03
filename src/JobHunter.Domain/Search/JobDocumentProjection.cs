namespace JobHunter.Domain.Search;

/// <summary>
/// The pure projection from a <see cref="JobProjectionSource"/> to the indexed <see cref="JobDocument"/>
/// (SAD §6.1). It is a pure function of its input — no clock, no infrastructure, unit-testable with no
/// network (T01 "projection is a pure function of its inputs") — and it is the <em>only</em> way a
/// <c>JobDocument</c> is constructed on the indexing path, so the allowlist is the whole of what can be
/// indexed. An absent score becomes <c>0</c> (a job indexed before it was ranked still appears, and
/// ranking updates it later — test-plan §edge cases); timestamps become Unix seconds because that is
/// what Typesense sorts an <c>int64</c> on.
/// </summary>
public static class JobDocumentProjection
{
    public static JobDocument ToDocument(JobProjectionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new JobDocument(
            Id: source.Id.ToString(),
            Title: source.Title,
            CompanyName: source.CompanyName,
            CompanyDomain: source.CompanyDomain,
            Description: source.Description,
            Technologies: source.Technologies,
            Countries: source.Countries,
            RemotePolicy: source.RemotePolicy,
            Seniority: source.Seniority,
            EmploymentType: source.EmploymentType,
            CompanyStage: source.CompanyStage,
            AiUsage: source.AiUsage,
            SalaryMin: source.SalaryMin,
            SalaryMax: source.SalaryMax,
            SalaryCurrency: source.SalaryCurrency,
            Score: source.Score ?? 0d,
            PostedAt: source.PostedAt?.ToUnixTimeSeconds(),
            FirstSeenAt: source.FirstSeenAt.ToUnixTimeSeconds(),
            Status: source.Status,
            ApplicationStatus: source.ApplicationStatus);
    }
}
