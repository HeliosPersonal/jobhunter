using System.Globalization;
using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Jobs;
using JobHunter.Infrastructure.Persistence.Jobs;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of "the full content of the jobs an enrichment batch will assess" (data-model §jobs,
/// SAD §6.2). Unlike <see cref="LiveJobsQuery"/>, it carries the posting <c>description</c> and the
/// company facts the prompt quotes, because the submission step renders — and therefore prices — a prompt
/// per job before deciding to submit (QG-2). Dapper, read-only (architecture rule 4 forbids a write here);
/// implements the port. It joins <c>companies</c> for the display name and canonical domain, and returns
/// only <c>Live</c> jobs — a closed or quarantined job is never enriched.
///
/// <para>The scope is the discovery window <c>[cutoffFrom, cutoffTo]</c> unioned with the specific
/// carried-over job ids (the previous Run's failed items retrying once, AC-08), so a transient failure is
/// not a permanent loss. The result is deduplicated and ordered by <c>first_seen_at</c> so the estimate
/// and the submission see the same items in the same order. The location summary and published-salary
/// text are composed here from the stored columns; nothing about the Owner is selected (invariant — the
/// CV crosses one boundary, F4's).</para>
/// </summary>
public sealed class EnrichmentScopeQuery(INpgsqlConnectionFactory connectionFactory) : IEnrichmentScopeQuery
{
    private const string Sql =
        """
        SELECT j.id                AS Id,
               c.display_name      AS CompanyName,
               c.canonical_domain  AS CanonicalDomain,
               j.title             AS Title,
               j.locations         AS LocationsJson,
               j.remote_policy     AS RemotePolicy,
               j.salary_raw        AS SalaryRaw,
               j.salary_min        AS SalaryMin,
               j.salary_max        AS SalaryMax,
               j.salary_currency   AS SalaryCurrency,
               j.salary_period     AS SalaryPeriod,
               j.employment_type   AS EmploymentType,
               j.description        AS Description,
               j.first_seen_at     AS FirstSeenAt
        FROM jobs j
        JOIN companies c ON c.id = j.company_id
        WHERE j.status = 'Live'
          AND ((j.first_seen_at >= @CutoffFrom AND j.first_seen_at <= @CutoffTo)
               OR j.id = ANY(@CarriedOverJobIds))
        ORDER BY j.first_seen_at DESC, j.id
        """;

    public async Task<IReadOnlyList<EnrichmentJobContent>> InScopeAsync(
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
            .Select(r => new EnrichmentJobContent(
                r.Id,
                r.CompanyName,
                r.CanonicalDomain,
                r.Title,
                SummariseLocation(r.RemotePolicy, r.LocationsJson),
                SummariseSalary(r.SalaryRaw, r.SalaryMin, r.SalaryMax, r.SalaryCurrency, r.SalaryPeriod),
                r.EmploymentType,
                r.Description ?? string.Empty))
            .ToList();
    }

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
        string? LocationsJson,
        string RemotePolicy,
        string? SalaryRaw,
        decimal? SalaryMin,
        decimal? SalaryMax,
        string? SalaryCurrency,
        string? SalaryPeriod,
        string EmploymentType,
        string? Description,
        DateTimeOffset FirstSeenAt);
}
