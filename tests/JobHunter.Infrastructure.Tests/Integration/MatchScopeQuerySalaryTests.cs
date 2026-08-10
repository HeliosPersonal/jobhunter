using JobHunter.Domain.Companies;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// The match scope's row→<see cref="MatchJobContent"/> projection formats a job's published pay and location
/// exactly as the match prompt will quote them (F4 SAD §6.1). This suite drives the summary arms the primary
/// suite does not: a verbatim <c>salary_raw</c> takes precedence over the structured columns; a structured
/// range with no raw is formatted with its currency and period; the single-figure and no-period arms; a blank
/// currency drops the prefix; a location-less job summarises to its bare remote policy; and the attached
/// enrichment's estimated salary is reconstructed or dropped when a bound is missing. Requires Docker.
/// </summary>
public sealed class MatchScopeQuerySalaryTests
{
    private static readonly DateTimeOffset WindowStart = new(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset InWindow = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task A_verbatim_salary_raw_is_returned_trimmed_and_takes_precedence()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var jobId = await SeedJobAsync(
            database, companyId,
            salary: SalaryRange.TryCreate(100_000m, 120_000m, "USD", SalaryPeriod.Year).Value,
            salaryRaw: "  €90k–120k  ");

        var job = (await Query(database).InScopeAsync(WindowStart, WindowEnd, [])).ShouldHaveSingleItem();

        job.PublishedSalary.ShouldBe("€90k–120k");
    }

    [RequiresDockerFact]
    public async Task A_structured_range_with_no_raw_is_formatted_with_currency_and_period()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var jobId = await SeedJobAsync(
            database, companyId,
            salary: SalaryRange.TryCreate(100_000m, 120_000m, "USD", SalaryPeriod.Year).Value,
            salaryRaw: null);

        var job = (await Query(database).InScopeAsync(WindowStart, WindowEnd, [])).ShouldHaveSingleItem();

        job.PublishedSalary.ShouldNotBeNull();
        job.PublishedSalary!.ShouldContain("USD");
        job.PublishedSalary.ShouldContain("100000");
        job.PublishedSalary.ShouldContain("120000");
        job.PublishedSalary.ShouldContain("Year");
    }

    [RequiresDockerFact]
    public async Task A_single_bound_and_no_period_summarises_the_lone_figure_without_a_suffix()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var jobId = await SeedJobAsync(
            database, companyId,
            salary: SalaryRange.TryCreate(95_000m, null, "EUR", SalaryPeriod.Year).Value,
            salaryRaw: null);
        // The domain always stores a point range and a period; null the upper bound and period on disk to
        // drive the single-figure and no-period arms — the shape a partially-parsed row can still take.
        await ExecuteAsync(database, "UPDATE jobs SET salary_max = NULL, salary_period = NULL WHERE id = @jobId", jobId);

        var job = (await Query(database).InScopeAsync(WindowStart, WindowEnd, [])).ShouldHaveSingleItem();

        job.PublishedSalary.ShouldNotBeNull();
        job.PublishedSalary!.ShouldContain("EUR");
        job.PublishedSalary.ShouldContain("95000");
        job.PublishedSalary.ShouldNotContain("-");
        job.PublishedSalary.ShouldNotContain("/");
    }

    [RequiresDockerFact]
    public async Task A_range_with_a_blank_currency_summarises_without_a_currency_prefix()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var jobId = await SeedJobAsync(
            database, companyId,
            salary: SalaryRange.TryCreate(100_000m, 120_000m, "USD", SalaryPeriod.Year).Value,
            salaryRaw: null);
        await ExecuteAsync(database, "UPDATE jobs SET salary_currency = '' WHERE id = @jobId", jobId);

        var job = (await Query(database).InScopeAsync(WindowStart, WindowEnd, [])).ShouldHaveSingleItem();

        job.PublishedSalary.ShouldNotBeNull();
        job.PublishedSalary!.ShouldStartWith("100000");
    }

    [RequiresDockerFact]
    public async Task A_job_with_no_locations_summarises_to_its_bare_remote_policy()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var jobId = await SeedJobAsync(database, companyId, salary: null, salaryRaw: null);
        // Empty the locations JSON to drive the "no places" arm: the summary is the remote policy alone.
        await ExecuteAsync(database, "UPDATE jobs SET locations = '[]' WHERE id = @jobId", jobId);

        var job = (await Query(database).InScopeAsync(WindowStart, WindowEnd, [])).ShouldHaveSingleItem();

        job.LocationSummary.ShouldBe("Remote");
        job.LocationSummary.ShouldNotContain("—");
    }

    [RequiresDockerFact]
    public async Task An_enrichment_missing_a_salary_bound_reconstructs_without_an_estimate()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database);
        var jobId = await SeedJobAsync(database, companyId, salary: null, salaryRaw: null);
        await SeedEnrichmentWithEstimateAsync(
            database, jobId, runId,
            SalaryEstimate.TryCreate(130_000m, 160_000m, "USD", SalaryPeriod.Year, 0.8m).Value);
        // Null just the upper estimate bound: ToEstimate takes the "not both present" arm and drops the salary
        // while keeping the rest of the enrichment (AC-09).
        await ExecuteAsync(database, "UPDATE enrichments SET salary_max = NULL WHERE job_id = @jobId", jobId);

        var job = (await Query(database).InScopeAsync(WindowStart, WindowEnd, [])).ShouldHaveSingleItem();

        job.Enrichment.ShouldNotBeNull();
        job.Enrichment!.EstimatedSalary.ShouldBeNull();
    }

    private static MatchScopeQuery Query(TestDatabase database) =>
        new(new NpgsqlConnectionFactory(database.ConnectionString));

    private static async Task ExecuteAsync(TestDatabase database, string sql, Guid jobId)
    {
        var factory = new NpgsqlConnectionFactory(database.ConnectionString);
        await using var connection = await factory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "jobId";
        parameter.Value = jobId;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> SeedCompanyAsync(TestDatabase database)
    {
        var companyId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, WindowStart));
        await ctx.SaveChangesAsync();
        return companyId;
    }

    private static async Task<Guid> SeedRunAsync(TestDatabase database)
    {
        var runId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        ctx.Add(new Run(runId, WindowStart.AddDays(-1), WindowStart, ceilingUsd: 5m, WindowStart));
        await ctx.SaveChangesAsync();
        return runId;
    }

    private static async Task SeedEnrichmentWithEstimateAsync(
        TestDatabase database, Guid jobId, Guid runId, SalaryEstimate? estimate)
    {
        var repo = new EnrichmentRepository(
            database.CreateContext(), new NpgsqlConnectionFactory(database.ConnectionString));
        var enrichment = new Enrichment(
            Guid.CreateVersion7(), jobId, runId, salary: estimate, isRemote: true, isContractorFriendly: false,
            TimezoneBand.EMEA, AiUsageLevel.None,
            new AiSignals(buildsAiProduct: false, buildsAiInfra: false, usesAiTooling: false, isResearch: false),
            CompanyStage.SeriesA, RoleFamily.Platform, technologies: ["Go", "Kubernetes"],
            reasons: ["seeded"], promptVersion: "enrich-v1", createdAt: InWindow);
        await repo.UpsertAsync(enrichment);
    }

    private static async Task<Guid> SeedJobAsync(
        TestDatabase database, Guid companyId, SalaryRange? salary, string? salaryRaw)
    {
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var rawPostingId = Guid.CreateVersion7();
        var jobId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, $"acme-{jobId:N}", BindingConfidence.TryCreate(0.9m).Value, "{}", InWindow));
        ctx.Add(new JobSource(sourceId, companyId, bindingId, $"https://boards-api.greenhouse.io/v1/boards/acme-{jobId:N}/jobs"));
        ctx.Add(new RawPosting(rawPostingId, sourceId, $"job-{jobId:N}", ContentHash.Compute($"{{\"t\":\"{jobId:N}\"}}"), "{\"t\":\"x\"}", 200, InWindow));
        ctx.Add(new Job(
            jobId, companyId, rawPostingId, Fingerprint.TryCreate(jobId.ToString("N") + Guid.NewGuid().ToString("N")).Value,
            fingerprintVersion: 1, "Staff SRE", normalisedTitle: "staff sre", description: "We keep the lights on.",
            applyUrl: $"https://acme.com/apply/{jobId:N}",
            LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            RemotePolicy.Remote, EmploymentType.FullTime, PostedAtGranularity.Day,
            firstSeenAt: InWindow, lastSeenAt: InWindow, salary: salary, salaryRaw: salaryRaw, status: JobStatus.Live));
        await ctx.SaveChangesAsync();
        return jobId;
    }
}
