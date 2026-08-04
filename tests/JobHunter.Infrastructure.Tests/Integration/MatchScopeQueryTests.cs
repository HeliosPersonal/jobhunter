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
/// T05: the read side of a matching batch's scope (F4 SAD §6.1). It is the enrichment-scope query's twin,
/// so it repeats the Live/window/carried-over guarantees (AC-08) — a closed job is never matched, a job
/// outside the window is only matched when carried over — and adds the property that makes it a match scope:
/// each job carries the latest enrichment attached by a <c>LEFT JOIN LATERAL</c>, and a job with none comes
/// back with a null enrichment rather than being dropped, so no job is lost for lacking an assessment (AC-09).
/// The latest row wins when a job has been enriched by more than one Run. Requires Docker.
/// </summary>
public sealed class MatchScopeQueryTests
{
    private static readonly DateTimeOffset WindowStart = new(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset InWindow = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BeforeWindow = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task InScope_returns_a_live_job_with_its_latest_enrichment_attached()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database);
        var jobId = await SeedJobAsync(
            database, companyId, seenAt: InWindow, status: JobStatus.Live,
            salary: SalaryRange.TryCreate(80_000m, 100_000m, "EUR", SalaryPeriod.Year).Value);
        await SeedEnrichmentAsync(database, jobId, runId);

        var query = new MatchScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var scope = await query.InScopeAsync(WindowStart, WindowEnd, []);

        var job = scope.ShouldHaveSingleItem();
        job.JobId.ShouldBe(jobId);
        job.CompanyName.ShouldBe("Acme");
        job.CanonicalDomain.ShouldBe("acme.com");
        job.Title.ShouldBe("Staff SRE");
        job.LocationSummary.ShouldContain("Germany");
        job.PublishedSalary.ShouldNotBeNull();
        job.PublishedSalary.ShouldContain("EUR");

        job.Enrichment.ShouldNotBeNull();
        job.Enrichment.CompanyStage.ShouldBe(CompanyStage.SeriesB);
        job.Enrichment.AiUsage.ShouldBe(AiUsageLevel.Medium);
        job.Enrichment.TimezoneBand.ShouldBe(TimezoneBand.EMEA);
        job.Enrichment.IsRemote.ShouldBeTrue();
        job.Enrichment.Technologies.ShouldBe(["C#", ".NET"]);
        job.Enrichment.EstimatedSalary.ShouldNotBeNull();
        job.Enrichment.EstimatedSalary.Min.ShouldBe(120_000m);
    }

    [RequiresDockerFact]
    public async Task InScope_returns_an_enrichment_less_job_with_a_null_enrichment_rather_than_dropping_it()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var jobId = await SeedJobAsync(database, companyId, seenAt: InWindow, status: JobStatus.Live);

        var query = new MatchScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var scope = await query.InScopeAsync(WindowStart, WindowEnd, []);

        // AC-09: the job is present, with a null enrichment, never lost for lacking an assessment.
        var job = scope.ShouldHaveSingleItem();
        job.JobId.ShouldBe(jobId);
        job.Enrichment.ShouldBeNull();
    }

    [RequiresDockerFact]
    public async Task InScope_attaches_the_latest_enrichment_when_a_job_was_enriched_by_more_than_one_run()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var jobId = await SeedJobAsync(database, companyId, seenAt: InWindow, status: JobStatus.Live);

        var firstRun = await SeedRunAsync(database, startedAt: InWindow.AddDays(-2), endedAt: InWindow.AddDays(-1));
        await SeedEnrichmentAsync(
            database, jobId, firstRun, createdAt: InWindow.AddDays(-1),
            companyStage: CompanyStage.Seed, aiUsage: AiUsageLevel.None, terminate: true);

        var secondRun = await SeedRunAsync(database, startedAt: InWindow.AddHours(-2), endedAt: InWindow);
        await SeedEnrichmentAsync(
            database, jobId, secondRun, createdAt: InWindow,
            companyStage: CompanyStage.SeriesB, aiUsage: AiUsageLevel.Medium);

        var query = new MatchScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var scope = await query.InScopeAsync(WindowStart, WindowEnd, []);

        var job = scope.ShouldHaveSingleItem();
        job.Enrichment.ShouldNotBeNull();
        job.Enrichment.CompanyStage.ShouldBe(CompanyStage.SeriesB);
        job.Enrichment.AiUsage.ShouldBe(AiUsageLevel.Medium);
    }

    [RequiresDockerFact]
    public async Task InScope_excludes_closed_jobs()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        await SeedJobAsync(database, companyId, seenAt: InWindow, status: JobStatus.Closed);

        var query = new MatchScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
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

        var query = new MatchScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
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

        var query = new MatchScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
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

        var query = new MatchScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
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

    private static async Task<Guid> SeedRunAsync(
        TestDatabase database,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null)
    {
        var runId = Guid.CreateVersion7();
        var start = startedAt ?? WindowStart.AddDays(-1);
        var end = endedAt ?? WindowStart;
        await using var ctx = database.CreateContext();
        ctx.Add(new Run(runId, start, end, ceilingUsd: 5m, end));
        await ctx.SaveChangesAsync();
        return runId;
    }

    private static async Task SeedEnrichmentAsync(
        TestDatabase database,
        Guid jobId,
        Guid runId,
        DateTimeOffset? createdAt = null,
        CompanyStage companyStage = CompanyStage.SeriesB,
        AiUsageLevel aiUsage = AiUsageLevel.Medium,
        bool terminate = false)
    {
        var repo = new EnrichmentRepository(
            database.CreateContext(), new NpgsqlConnectionFactory(database.ConnectionString));
        var enrichment = new Enrichment(
            Guid.CreateVersion7(), jobId, runId,
            SalaryEstimate.TryCreate(120_000m, 160_000m, "USD", SalaryPeriod.Year, 0.7m).Value,
            isRemote: true, isContractorFriendly: false, TimezoneBand.EMEA, aiUsage,
            new AiSignals(buildsAiProduct: false, buildsAiInfra: true, usesAiTooling: true, isResearch: false),
            companyStage, RoleFamily.Platform, technologies: ["C#", ".NET"],
            reasons: ["Salary band inferred from peers."],
            promptVersion: "enrich-v1", createdAt ?? InWindow);
        await repo.UpsertAsync(enrichment);

        if (terminate)
        {
            // Retire this Run so the single-active-run index allows the next one to be created.
            var runRepo = new RunRepository(database.CreateContext());
            var run = await runRepo.FindAsync(runId);
            run!.Abort("done", (createdAt ?? InWindow).AddMinutes(1), costBreach: false);
            await runRepo.SaveChangesAsync();
        }
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
