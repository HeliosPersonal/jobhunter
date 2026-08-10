using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// The enrichment scope composes each job's published pay and location from the stored columns exactly as
/// the enrichment prompt will quote them (SAD §6.2). The primary suite covers a structured EUR range and a
/// no-salary job; this one drives the remaining summary arms: a verbatim <c>salary_raw</c> takes precedence
/// and is trimmed, the single-figure and no-period arms, a blank currency drops the prefix, and a
/// location-less job summarises to its bare remote policy. Requires Docker.
/// </summary>
public sealed class EnrichmentScopeQuerySalaryTests
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
        await SeedJobAsync(
            database, companyId,
            salary: SalaryRange.TryCreate(100_000m, 120_000m, "USD", SalaryPeriod.Year).Value,
            salaryRaw: "  €90k–120k  ");

        var job = (await Query(database).InScopeAsync(WindowStart, WindowEnd, [])).ShouldHaveSingleItem();

        job.PublishedSalary.ShouldBe("€90k–120k");
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
        await ExecuteAsync(database, "UPDATE jobs SET locations = '[]' WHERE id = @jobId", jobId);

        var job = (await Query(database).InScopeAsync(WindowStart, WindowEnd, [])).ShouldHaveSingleItem();

        job.LocationSummary.ShouldBe("Remote");
        job.LocationSummary.ShouldNotContain("—");
    }

    private static EnrichmentScopeQuery Query(TestDatabase database) =>
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
