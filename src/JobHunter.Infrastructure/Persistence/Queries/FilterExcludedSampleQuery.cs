using System.Globalization;
using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Infrastructure.Persistence.Jobs;
using JobHunter.Infrastructure.Persistence.Pipeline;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of the regret sampler (F4 T21, ADR-F4-0003): a sample of the jobs the pre-match filter
/// excluded from the latest Run's deep tier, reconstructed as the <see cref="MatchJobContent"/> a real match
/// would have judged. Implements <see cref="IFilterExcludedSampleQuery"/> with Dapper, read-only (architecture
/// rule 4 forbids a write here). It is the <see cref="MatchScopeQuery"/> projection narrowed to the filtered
/// set, so the sampler prices and matches the excluded jobs from exactly the same content the pipeline would.
///
/// <para>A pre-match exclusion is a <c>suppressed</c> score of the latest Run — the one with the greatest
/// <c>started_at</c> — that has <strong>no <c>matches</c> row</strong> for that job and Run (<c>NOT EXISTS</c>).
/// That is the ADR's own definition: a factually filtered job never reaches the deep tier and so never gets a
/// match, whereas a post-ranking suppression is always scored from a match and is therefore excluded from the
/// sample. Ordered by <c>job_id</c> for a deterministic, capped sample; the job's latest enrichment (any Run)
/// is attached with the same <c>LEFT JOIN LATERAL</c> the match scope uses, or null when it has none (AC-09).
/// It selects <strong>nothing about the Owner's CV</strong> — the CV crosses exactly one boundary, not this one.</para>
/// </summary>
public sealed class FilterExcludedSampleQuery(INpgsqlConnectionFactory connectionFactory) : IFilterExcludedSampleQuery
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
        FROM scores s
        JOIN jobs j ON j.id = s.job_id
        JOIN companies c ON c.id = j.company_id
        LEFT JOIN LATERAL (
            SELECT en.*
            FROM enrichments en
            WHERE en.job_id = j.id
            ORDER BY en.created_at DESC
            LIMIT 1
        ) e ON TRUE
        WHERE s.suppressed
          AND s.run_id = (
              SELECT r.id FROM runs r
              ORDER BY r.started_at DESC NULLS LAST
              LIMIT 1
          )
          AND NOT EXISTS (
              SELECT 1 FROM matches m
              WHERE m.job_id = s.job_id AND m.run_id = s.run_id
          )
        ORDER BY s.job_id
        LIMIT @Limit
        """;

    public async Task<IReadOnlyList<MatchJobContent>> SampleAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { Limit = limit }, cancellationToken: cancellationToken);
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
        // A null enrichment id means the LEFT JOIN LATERAL found no assessment: the enrichment lines are
        // omitted and the eventual score discounted, but the job is never dropped (AC-09).
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
