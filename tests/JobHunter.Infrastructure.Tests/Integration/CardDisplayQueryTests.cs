using JobHunter.Domain.Companies;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.TestKit;
using Shouldly;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F5 T12: the read side the production <c>DigestRenderer</c> draws the card's <em>display</em> facts from.
/// A <c>DigestCard</c> snapshots the score and reasons (invariant 4) but not the title, company, location or
/// salary the card shows — those are the job's own, joined at render time. This query returns them per job id:
/// the job's title, apply URL, published salary and locations; its company's display name and stage; and — for
/// the <c>(est)</c> salary the card falls back to when nothing is published — the job's most recent enrichment
/// estimate. The load-bearing properties: only the latest enrichment estimate is surfaced (a superseded
/// assessment does not); a job absent from the store yields no entry rather than a fabricated row; and it
/// selects <strong>nothing about the Owner</strong> (the CV crosses exactly one boundary, and it is not this
/// one). Read-only — Dapper never writes (architecture rule 4). Requires Docker.
/// </summary>
public sealed class CardDisplayQueryTests
{
    private static readonly DateTimeOffset FirstSeen = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RunStart = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task DisplayFacts_returns_the_job_and_company_facts_a_card_shows()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var jobId = await SeedJobAsync(
            database, companyId, salary: SalaryRange.TryCreate(150_000m, 180_000m, "USD", SalaryPeriod.Year).Value);

        var query = new CardDisplayQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var facts = await query.DisplayFactsAsync([jobId]);

        var card = facts[jobId];
        card.JobId.ShouldBe(jobId);
        card.Title.ShouldBe("Staff SRE");
        card.Company.ShouldBe("Acme");
        card.Stage.ShouldBe("Series B");
        card.Countries.ShouldBe(["Germany"]);
        card.RemotePolicy.ShouldBe("Remote");
        card.PublishedSalaryMin.ShouldBe(150_000);
        card.PublishedSalaryMax.ShouldBe(180_000);
        card.PublishedSalaryCurrency.ShouldBe("USD");
        card.ApplyUrl.ShouldBe($"https://acme.com/apply/{jobId:N}");
        // The header top-opportunity highlights come from the job's deterministic technology tags, sorted.
        card.Highlights.ShouldBe(["Go", "Kubernetes"]);
    }

    [RequiresDockerFact]
    public async Task DisplayFacts_surfaces_the_latest_enrichment_estimate_for_the_est_salary_line()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database);
        // No published salary: the card falls back to the model's estimate, marked (est).
        var jobId = await SeedJobAsync(database, companyId, salary: null);
        await SeedEnrichmentAsync(
            database, jobId, runId, RunStart,
            SalaryEstimate.TryCreate(120_000m, 140_000m, "USD", SalaryPeriod.Year, confidence: 0.6m).Value);

        var query = new CardDisplayQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var facts = await query.DisplayFactsAsync([jobId]);

        var card = facts[jobId];
        card.PublishedSalaryMin.ShouldBeNull();
        card.EstimatedSalaryMin.ShouldBe(120_000);
        card.EstimatedSalaryMax.ShouldBe(140_000);
        card.EstimatedSalaryCurrency.ShouldBe("USD");
        card.EstimatedSalaryConfidence.ShouldBe(0.6m);
    }

    [RequiresDockerFact]
    public async Task DisplayFacts_takes_the_estimate_from_the_most_recent_enrichment()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var oldRun = await SeedRunAsync(database);
        var newRun = await SeedRunAsync(database);
        var jobId = await SeedJobAsync(database, companyId, salary: null);
        await SeedEnrichmentAsync(
            database, jobId, oldRun, RunStart,
            SalaryEstimate.TryCreate(90_000m, 100_000m, "USD", SalaryPeriod.Year, confidence: 0.3m).Value);
        await SeedEnrichmentAsync(
            database, jobId, newRun, RunStart.AddHours(1),
            SalaryEstimate.TryCreate(120_000m, 140_000m, "USD", SalaryPeriod.Year, confidence: 0.6m).Value);

        var query = new CardDisplayQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var facts = await query.DisplayFactsAsync([jobId]);

        // The most recent assessment wins; a superseded estimate does not leak through.
        facts[jobId].EstimatedSalaryMin.ShouldBe(120_000);
        facts[jobId].EstimatedSalaryConfidence.ShouldBe(0.6m);
    }

    [RequiresDockerFact]
    public async Task DisplayFacts_omits_a_job_that_is_not_in_the_store()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var present = await SeedJobAsync(database, companyId, salary: null);
        var absent = Guid.CreateVersion7();

        var query = new CardDisplayQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var facts = await query.DisplayFactsAsync([present, absent]);

        // A job with no row yields no entry — the renderer skips it rather than showing a fabricated card.
        facts.ContainsKey(present).ShouldBeTrue();
        facts.ContainsKey(absent).ShouldBeFalse();
    }

    private static async Task<Guid> SeedCompanyAsync(TestDatabase database)
    {
        var companyId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        var company = new Company(
            companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, RunStart);
        ctx.Add(company);
        // Stage is set by F3, not at construction; set it through EF for the card's "· Series B" line.
        ctx.Entry(company).Property(nameof(Company.Stage)).CurrentValue = "Series B";
        await ctx.SaveChangesAsync();
        return companyId;
    }

    private static async Task<Guid> SeedRunAsync(TestDatabase database)
    {
        var runId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        var run = new Run(runId, RunStart.AddDays(-1), RunStart, ceilingUsd: 5m, RunStart);
        run.Abort("seeded", RunStart.AddMinutes(1), costBreach: false);
        ctx.Add(run);
        await ctx.SaveChangesAsync();
        return runId;
    }

    private static async Task<Guid> SeedJobAsync(TestDatabase database, Guid companyId, SalaryRange? salary)
    {
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var rawPostingId = Guid.CreateVersion7();
        var jobId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, $"acme-{jobId:N}", BindingConfidence.TryCreate(0.9m).Value, "{}", FirstSeen));
        ctx.Add(new JobSource(sourceId, companyId, bindingId, $"https://boards-api.greenhouse.io/v1/boards/acme-{jobId:N}/jobs"));
        ctx.Add(new RawPosting(rawPostingId, sourceId, $"job-{jobId:N}", ContentHash.Compute($"{{\"t\":\"{jobId:N}\"}}"), "{\"t\":\"x\"}", 200, FirstSeen));
        var job = new Job(
            jobId, companyId, rawPostingId, Fingerprint.TryCreate(jobId.ToString("N") + Guid.NewGuid().ToString("N")).Value,
            fingerprintVersion: 1, "Staff SRE", normalisedTitle: "staff sre", description: "We keep the lights on.",
            applyUrl: $"https://acme.com/apply/{jobId:N}",
            LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            RemotePolicy.Remote, EmploymentType.FullTime, PostedAtGranularity.Day,
            firstSeenAt: FirstSeen, lastSeenAt: FirstSeen, salary: salary, status: JobStatus.Live);
        job.AddTechnology("Kubernetes", TechnologyMatch.Description);
        job.AddTechnology("Go", TechnologyMatch.Vocabulary);
        ctx.Add(job);
        await ctx.SaveChangesAsync();
        return jobId;
    }

    private static async Task SeedEnrichmentAsync(
        TestDatabase database, Guid jobId, Guid runId, DateTimeOffset createdAt, SalaryEstimate salary)
    {
        await using var ctx = database.CreateContext();
        var enrichment = new Enrichment(
            Guid.CreateVersion7(), jobId, runId, salary, isRemote: true, isContractorFriendly: false,
            TimezoneBand.EMEA, AiUsageLevel.None, AiSignals.None, CompanyStage.SeriesB, RoleFamily.Other,
            technologies: ["Go"], reasons: ["Keeps the lights on."], promptVersion: "enrich-v1", createdAt);
        ctx.Add(enrichment);
        await ctx.SaveChangesAsync();
    }
}
