using System.Globalization;
using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Infrastructure.Persistence.Jobs;
using JobHunter.Infrastructure.Persistence.Pipeline;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of "the full content of the jobs a matching batch will assess" (data-model §jobs/§enrichments,
/// F4 SAD §6.1). The matching analogue of <see cref="EnrichmentScopeQuery"/>: it carries the posting text and
/// company facts a match prompt quotes <em>and</em> the latest enrichment for each job, because the submission
/// step renders — and therefore prices — a match prompt per job before deciding to submit (QG-2). Dapper,
/// read-only (architecture rule 4 forbids a write here); implements the port. It joins <c>companies</c> for
/// the display name and canonical domain, and returns only <c>Live</c> jobs — a closed or quarantined job is
/// never matched.
///
/// <para>The enrichment is attached with a <c>LEFT JOIN LATERAL</c> that takes the most recent row per job
/// across any Run (<c>idx_enrichments_job_latest</c>); a job with none comes back with a null enrichment
/// rather than being dropped, so a job is never lost for lacking an assessment (AC-09). The scope is the
/// discovery window <c>[cutoffFrom, cutoffTo]</c> unioned with the specific carried-over job ids (the previous
/// Run's failed items retrying once, AC-08), deduplicated and ordered by <c>first_seen_at</c> so the estimate
/// and the submission see the same items in the same order. Nothing about the Owner is selected — the CV
/// crosses exactly one boundary, and it is not this one (F4 invariant).</para>
/// </summary>
public sealed class MatchScopeQuery(INpgsqlConnectionFactory connectionFactory) : IMatchScopeQuery
{
    private const string Sql =
        """
        SELECT j.id                AS Id,
               c.display_name      AS CompanyName,
               c.canonical_domain  AS CanonicalDomain,
               j.title             AS Title,
               j.seniority         AS Seniority,
               j.locations         AS LocationsJson,
               j.remote_policy     AS RemotePolicy,
               j.salary_raw        AS SalaryRaw,
               j.salary_min        AS SalaryMin,
               j.salary_max        AS SalaryMax,
               j.salary_currency   AS SalaryCurrency,
               j.salary_period     AS SalaryPeriod,
               j.employment_type   AS EmploymentType,
               j.description        AS Description,
               j.first_seen_at     AS FirstSeenAt,
               e.id                AS EnrichmentId,
               e.salary_min        AS EstSalaryMin,
               e.salary_max        AS EstSalaryMax,
               e.salary_currency   AS EstSalaryCurrency,
               e.salary_period     AS EstSalaryPeriod,
               e.salary_confidence AS EstSalaryConfidence,
               e.is_remote         AS IsRemote,
               e.is_contractor_friendly AS IsContractorFriendly,
               e.timezone_band     AS TimezoneBand,
               e.ai_usage          AS AiUsage,
               e.company_stage     AS CompanyStage,
               e.technologies      AS TechnologiesJson
        FROM jobs j
        JOIN companies c ON c.id = j.company_id
        LEFT JOIN LATERAL (
            SELECT en.*
            FROM enrichments en
            WHERE en.job_id = j.id
            ORDER BY en.created_at DESC
            LIMIT 1
        ) e ON TRUE
        WHERE j.status = 'Live'
          AND ((j.first_seen_at >= @CutoffFrom AND j.first_seen_at <= @CutoffTo)
               OR j.id = ANY(@CarriedOverJobIds))
        ORDER BY j.first_seen_at DESC, j.id
        """;

    public async Task<IReadOnlyList<MatchJobContent>> InScopeAsync(
        DateTimeOffset cutoffFrom,
        DateTimeOffset cutoffTo,
        IReadOnlyCollection<Guid> carriedOverJobIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(carriedOverJobIds);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var parameters = new
        {
            CutoffFrom = cutoffFrom,
            CutoffTo = cutoffTo,
            CarriedOverJobIds = carriedOverJobIds.ToArray(),
        };
        var command = new CommandDefinition(Sql, parameters, cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<ScopeRow>(command);

        return rows
            .Select(r => new MatchJobContent(
                r.Id,
                r.CompanyName,
                r.CanonicalDomain,
                r.Title,
                r.Seniority?.ToString(),
                SummariseLocation(r.RemotePolicy, r.LocationsJson),
                SummariseSalary(r.SalaryRaw, r.SalaryMin, r.SalaryMax, r.SalaryCurrency, r.SalaryPeriod),
                r.EmploymentType,
                r.Description ?? string.Empty,
                ToEnrichmentContent(r)))
            .ToList();
    }

    private static MatchEnrichmentContent? ToEnrichmentContent(ScopeRow r)
    {
        // A null enrichment id means the LEFT JOIN LATERAL found no assessment for the job: the prompt lines
        // are omitted and the eventual score is discounted, but the job is never dropped (AC-09).
        if (r.EnrichmentId is null)
        {
            return null;
        }

        return new MatchEnrichmentContent(
            CompanyStage: ParseEnum<CompanyStage>(r.CompanyStage, CompanyStage.Unknown),
            IsRemote: r.IsRemote ?? false,
            TimezoneBand: ParseEnum<TimezoneBand>(r.TimezoneBand, TimezoneBand.Unknown),
            IsContractorFriendly: r.IsContractorFriendly ?? false,
            EstimatedSalary: ToEstimate(r),
            Technologies: StringListJson.Deserialize(r.TechnologiesJson),
            AiUsage: ParseEnum<AiUsageLevel>(r.AiUsage, AiUsageLevel.None));
    }

    private static SalaryEstimate? ToEstimate(ScopeRow r)
    {
        if (r.EstSalaryMin is not { } min || r.EstSalaryMax is not { } max)
        {
            return null;
        }

        var period = ParseEnum<SalaryPeriod>(r.EstSalaryPeriod, SalaryPeriod.Year);
        var confidence = r.EstSalaryConfidence ?? 0m;
        var result = SalaryEstimate.TryCreate(min, max, r.EstSalaryCurrency, period, confidence);

        // The value was valid when it was persisted; if a stored row is somehow unrepresentable the salary is
        // simply dropped and the rest of the enrichment kept, never thrown (parsing step 5 in spirit).
        return result.IsSuccess ? result.Value : null;
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) ? parsed : fallback;

    private static string SummariseLocation(string remotePolicy, string? locationsJson)
    {
        var places = string.Empty;
        if (!string.IsNullOrWhiteSpace(locationsJson))
        {
            var set = LocationSetJson.Deserialize(locationsJson);
            places = string.Join("; ", set.Locations.Select(l => l.ToString()));
        }

        return string.IsNullOrEmpty(places) ? remotePolicy : $"{remotePolicy} — {places}";
    }

    private static string? SummariseSalary(
        string? salaryRaw,
        decimal? min,
        decimal? max,
        string? currency,
        string? period)
    {
        if (!string.IsNullOrWhiteSpace(salaryRaw))
        {
            return salaryRaw.Trim();
        }

        if (min is null && max is null)
        {
            return null;
        }

        var cur = string.IsNullOrWhiteSpace(currency) ? string.Empty : currency.Trim() + " ";
        var range = min is not null && max is not null
            ? string.Create(CultureInfo.InvariantCulture, $"{min}-{max}")
            : string.Create(CultureInfo.InvariantCulture, $"{min ?? max}");
        var per = string.IsNullOrWhiteSpace(period) ? string.Empty : " / " + period.Trim();
        return $"{cur}{range}{per}";
    }

    private sealed record ScopeRow(
        Guid Id,
        string CompanyName,
        string CanonicalDomain,
        string Title,
        Seniority? Seniority,
        string? LocationsJson,
        string RemotePolicy,
        string? SalaryRaw,
        decimal? SalaryMin,
        decimal? SalaryMax,
        string? SalaryCurrency,
        string? SalaryPeriod,
        string EmploymentType,
        string? Description,
        DateTimeOffset FirstSeenAt,
        Guid? EnrichmentId,
        decimal? EstSalaryMin,
        decimal? EstSalaryMax,
        string? EstSalaryCurrency,
        string? EstSalaryPeriod,
        decimal? EstSalaryConfidence,
        bool? IsRemote,
        bool? IsContractorFriendly,
        string? TimezoneBand,
        string? AiUsage,
        string? CompanyStage,
        string? TechnologiesJson);
}
