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
/// T10: the read side of an enrichment batch's scope (SAD §6.2). It proves the submit handler sees exactly
/// the jobs it should price and submit: Live jobs inside the discovery window, plus the previous Run's
/// carried-over items even when they fall outside it (AC-08); and never a closed or quarantined job. The
/// location summary and published-salary text are composed from the stored columns so the prompt reads the
/// same facts the Owner would. Requires Docker.
/// </summary>
public sealed class EnrichmentScopeQueryTests
{
    private static readonly DateTimeOffset WindowStart = new(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset InWindow = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BeforeWindow = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task InScope_returns_live_jobs_inside_the_window_with_composed_location_and_salary()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var jobId = await SeedJobAsync(
            database, companyId, seenAt: InWindow, status: JobStatus.Live,
            salary: SalaryRange.TryCreate(80_000m, 100_000m, "EUR", SalaryPeriod.Year).Value);

        var query = new EnrichmentScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var scope = await query.InScopeAsync(WindowStart, WindowEnd, []);

        var job = scope.ShouldHaveSingleItem();
        job.JobId.ShouldBe(jobId);
        job.CompanyName.ShouldBe("Acme");
        job.CanonicalDomain.ShouldBe("acme.com");
        job.Title.ShouldBe("Staff SRE");
        job.LocationSummary.ShouldContain("Germany");
        job.PublishedSalary.ShouldNotBeNull();
        job.PublishedSalary.ShouldContain("EUR");
        job.Description.ShouldNotBeNullOrWhiteSpace();
    }

    [RequiresDockerFact]
    public async Task InScope_excludes_closed_jobs()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        await SeedJobAsync(database, companyId, seenAt: InWindow, status: JobStatus.Closed);

        var query = new EnrichmentScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var scope = await query.InScopeAsync(WindowStart, WindowEnd, []);

        scope.ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task InScope_excludes_live_jobs_outside_the_window()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        await SeedJobAsync(database, companyId, seenAt: BeforeWindow, status: JobStatus.Live);

        var query = new EnrichmentScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var scope = await query.InScopeAsync(WindowStart, WindowEnd, []);

        scope.ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task InScope_includes_a_carried_over_job_even_outside_the_window()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var carried = await SeedJobAsync(database, companyId, seenAt: BeforeWindow, status: JobStatus.Live);

        var query = new EnrichmentScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var scope = await query.InScopeAsync(WindowStart, WindowEnd, [carried]);

        scope.ShouldHaveSingleItem().JobId.ShouldBe(carried);
    }

    [RequiresDockerFact]
    public async Task InScope_reports_no_salary_when_none_was_published()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        await SeedJobAsync(database, companyId, seenAt: InWindow, status: JobStatus.Live, salary: null);

        var query = new EnrichmentScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var scope = await query.InScopeAsync(WindowStart, WindowEnd, []);

        scope.ShouldHaveSingleItem().PublishedSalary.ShouldBeNull();
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
        TestDatabase database,
        Guid companyId,
        DateTimeOffset seenAt,
        JobStatus status,
        SalaryRange? salary = null)
    {
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var rawPostingId = Guid.CreateVersion7();
        var jobId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, $"acme-{jobId:N}", BindingConfidence.TryCreate(0.9m).Value, "{}", seenAt));
        ctx.Add(new JobSource(sourceId, companyId, bindingId, $"https://boards-api.greenhouse.io/v1/boards/acme-{jobId:N}/jobs"));
        ctx.Add(new RawPosting(rawPostingId, sourceId, $"job-{jobId:N}", ContentHash.Compute($"{{\"t\":\"{jobId:N}\"}}"), "{\"t\":\"x\"}", 200, seenAt));
        ctx.Add(new Job(
            jobId, companyId, rawPostingId, Fingerprint.TryCreate(new string('a', 63) + (status == JobStatus.Live ? "b" : "c")).Value,
            fingerprintVersion: 1, "Staff SRE", normalisedTitle: "staff sre", description: "We keep the lights on.",
            applyUrl: $"https://acme.com/apply/{jobId:N}",
            LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            RemotePolicy.Remote, EmploymentType.FullTime, PostedAtGranularity.Day,
            firstSeenAt: seenAt, lastSeenAt: seenAt, salary: salary, status: status));
        await ctx.SaveChangesAsync();
        return jobId;
    }
}
