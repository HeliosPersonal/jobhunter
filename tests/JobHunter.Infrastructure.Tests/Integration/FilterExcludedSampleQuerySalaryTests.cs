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

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// The regret sampler's row→<see cref="MatchJobContent"/> projection reconstructs the salary and location
/// exactly as the live match scope would (F4 T21), so the excluded jobs are priced from the same content the
/// pipeline judged. This suite drives the projection's summary arms that the happy-path suite does not: a
/// verbatim <c>salary_raw</c> takes precedence; a structured range with no raw is formatted with its currency
/// and period; a single-figure range mirrors its bound; a no-salary job yields null; and the attached
/// enrichment's estimated salary is reconstructed (or dropped when partial). Requires Docker.
/// </summary>
public sealed class FilterExcludedSampleQuerySalaryTests
{
    private static readonly DateTimeOffset FirstSeen = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RunStart = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task A_verbatim_salary_raw_is_returned_trimmed_and_takes_precedence()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        // Both a raw string and a structured range are present; the raw wins.
        var jobId = await SeedJobAsync(
            database, companyId,
            salary: SalaryRange.TryCreate(100000m, 120000m, "USD", SalaryPeriod.Year).Value,
            salaryRaw: "  €90k–120k  ");
        await SeedScoreAsync(database, jobId, runId);

        var job = (await Query(database).SampleAsync(limit: 20)).ShouldHaveSingleItem();

        job.PublishedSalary.ShouldBe("€90k–120k");
    }

    [RequiresDockerFact]
    public async Task A_structured_range_with_no_raw_is_formatted_with_currency_and_period()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var jobId = await SeedJobAsync(
            database, companyId,
            salary: SalaryRange.TryCreate(100000m, 120000m, "USD", SalaryPeriod.Year).Value,
            salaryRaw: null);
        await SeedScoreAsync(database, jobId, runId);

        var job = (await Query(database).SampleAsync(limit: 20)).ShouldHaveSingleItem();

        // "USD " prefix, the "min-max" range, and the " / Year" period suffix.
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
        var runId = await SeedRunAsync(database, RunStart);
        var jobId = await SeedJobAsync(
            database, companyId,
            salary: SalaryRange.TryCreate(95000m, null, "EUR", SalaryPeriod.Year).Value,
            salaryRaw: null);
        // The domain always stores a point range (min == max) and a period, so to drive the
        // single-figure and no-period arms of the summary we null the upper bound and the period
        // directly — the shape a legacy or partially-parsed row can still take on disk.
        await NullSalaryUpperBoundAndPeriodAsync(database, jobId);
        await SeedScoreAsync(database, jobId, runId);

        var job = (await Query(database).SampleAsync(limit: 20)).ShouldHaveSingleItem();

        job.PublishedSalary.ShouldNotBeNull();
        job.PublishedSalary!.ShouldContain("EUR");
        job.PublishedSalary.ShouldContain("95000");
        // No upper bound → a single figure, and no period → no " / …" suffix.
        job.PublishedSalary.ShouldNotContain("-");
        job.PublishedSalary.ShouldNotContain("/");
    }

    [RequiresDockerFact]
    public async Task A_range_with_a_blank_currency_summarises_without_a_currency_prefix()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var jobId = await SeedJobAsync(
            database, companyId,
            salary: SalaryRange.TryCreate(100000m, 120000m, "USD", SalaryPeriod.Year).Value,
            salaryRaw: null);
        // Blank the currency on disk to drive the empty-prefix arm of the summary.
        await BlankSalaryCurrencyAsync(database, jobId);
        await SeedScoreAsync(database, jobId, runId);

        var job = (await Query(database).SampleAsync(limit: 20)).ShouldHaveSingleItem();

        job.PublishedSalary.ShouldNotBeNull();
        job.PublishedSalary!.ShouldStartWith("100000");
    }

    [RequiresDockerFact]
    public async Task A_job_with_no_salary_at_all_yields_null_salary_text()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var jobId = await SeedJobAsync(database, companyId, salary: null, salaryRaw: null);
        await SeedScoreAsync(database, jobId, runId);

        var job = (await Query(database).SampleAsync(limit: 20)).ShouldHaveSingleItem();

        job.PublishedSalary.ShouldBeNull();
    }

    [RequiresDockerFact]
    public async Task The_attached_enrichments_estimated_salary_is_reconstructed()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var jobId = await SeedJobAsync(database, companyId, salary: null, salaryRaw: null);
        await SeedEnrichmentWithEstimateAsync(
            database, jobId, runId,
            SalaryEstimate.TryCreate(130000m, 160000m, "USD", SalaryPeriod.Year, 0.8m).Value);
        await SeedScoreAsync(database, jobId, runId);

        var job = (await Query(database).SampleAsync(limit: 20)).ShouldHaveSingleItem();

        job.Enrichment.ShouldNotBeNull();
        job.Enrichment!.EstimatedSalary.ShouldNotBeNull();
        job.Enrichment.EstimatedSalary!.Min.ShouldBe(130000m);
        job.Enrichment.EstimatedSalary.Max.ShouldBe(160000m);
    }

    [RequiresDockerFact]
    public async Task An_enrichment_with_no_estimated_salary_reconstructs_without_one()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var jobId = await SeedJobAsync(database, companyId, salary: null, salaryRaw: null);
        await SeedEnrichmentWithEstimateAsync(database, jobId, runId, estimate: null);
        await SeedScoreAsync(database, jobId, runId);

        var job = (await Query(database).SampleAsync(limit: 20)).ShouldHaveSingleItem();

        job.Enrichment.ShouldNotBeNull();
        job.Enrichment!.EstimatedSalary.ShouldBeNull();
    }

    private static FilterExcludedSampleQuery Query(TestDatabase database) =>
        new(new NpgsqlConnectionFactory(database.ConnectionString));

    private static Task NullSalaryUpperBoundAndPeriodAsync(TestDatabase database, Guid jobId) =>
        ExecuteAsync(database, "UPDATE jobs SET salary_max = NULL, salary_period = NULL WHERE id = @jobId", jobId);

    private static Task BlankSalaryCurrencyAsync(TestDatabase database, Guid jobId) =>
        ExecuteAsync(database, "UPDATE jobs SET salary_currency = '' WHERE id = @jobId", jobId);

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
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, RunStart));
        await ctx.SaveChangesAsync();
        return companyId;
    }

    private static async Task<Guid> SeedRunAsync(TestDatabase database, DateTimeOffset startedAt)
    {
        var runId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        var run = new Run(runId, startedAt.AddDays(-1), startedAt, ceilingUsd: 5m, startedAt);
        run.Abort("seeded", startedAt.AddMinutes(1), costBreach: false);
        ctx.Add(run);
        await ctx.SaveChangesAsync();
        return runId;
    }

    private static async Task<Guid> SeedJobAsync(
        TestDatabase database, Guid companyId, SalaryRange? salary, string? salaryRaw)
    {
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var rawPostingId = Guid.CreateVersion7();
        var jobId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, $"acme-{jobId:N}", BindingConfidence.TryCreate(0.9m).Value, "{}", FirstSeen));
        ctx.Add(new JobSource(sourceId, companyId, bindingId, $"https://boards-api.greenhouse.io/v1/boards/acme-{jobId:N}/jobs"));
        ctx.Add(new RawPosting(rawPostingId, sourceId, $"job-{jobId:N}", ContentHash.Compute($"{{\"t\":\"{jobId:N}\"}}"), "{\"t\":\"x\"}", 200, FirstSeen));
        ctx.Add(new Job(
            jobId, companyId, rawPostingId, Fingerprint.TryCreate(jobId.ToString("N") + Guid.NewGuid().ToString("N")).Value,
            fingerprintVersion: 1, "Staff SRE", normalisedTitle: "staff sre", description: "We keep the lights on.",
            applyUrl: $"https://acme.com/apply/{jobId:N}",
            LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            RemotePolicy.Remote, EmploymentType.FullTime, PostedAtGranularity.Day,
            firstSeenAt: FirstSeen, lastSeenAt: FirstSeen, salary: salary, salaryRaw: salaryRaw, status: JobStatus.Live));
        await ctx.SaveChangesAsync();
        return jobId;
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
            reasons: ["seeded"], promptVersion: "enrich-v1", createdAt: RunStart);
        await repo.UpsertAsync(enrichment);
    }

    private static async Task SeedScoreAsync(TestDatabase database, Guid jobId, Guid runId)
    {
        var components = new ScoreComponents(
            match: 0m, alignment: 0m, preference: 0m, freshness: 0m, confidenceMultiplier: 1.00m);
        var score = new Score(
            jobId, runId, finalScore: 0m, components, RankingWeights.Default, preferenceModelId: null,
            suppressed: true, suppressionReason: "Employment type not sought", RunStart);

        var repo = new ScoreRepository(
            database.CreateContext(), new NpgsqlConnectionFactory(database.ConnectionString));
        await repo.UpsertAsync(score);
    }
}
