using JobHunter.Domain.Companies;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Research;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Shouldly;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F8 T09 C3: the read side of the on-demand <c>/company</c> command and the research API
/// (<see cref="ICompanyResearchQuery"/>, SAD §6.2). It resolves a company by <c>display_name</c> and returns
/// its latest dossier — the newest by <c>generated_at</c>, served by <c>idx_research_company_latest</c> — with
/// every claim joined back to its source for the URL invariant 5 requires, warnings first (AC-04). The
/// load-bearing properties asserted here: an unknown name is null (kept distinct from a known company with no
/// dossier, which is a lookup with a null dossier); only the latest dossier is returned; a claim carries its
/// source URL and observed date; and the categories that produced nothing are surfaced (AC-07). Read-only —
/// Dapper never writes (architecture rule 4). Requires Docker.
/// </summary>
public sealed class CompanyResearchQueryTests
{
    private static readonly DateTimeOffset RunStart = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 4, 6, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task An_unknown_company_name_resolves_to_no_candidates()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;

        var query = new CompanyResearchQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        (await query.ResolveCandidatesAsync("Nowhere Inc")).ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task A_known_company_with_no_dossier_resolves_with_a_null_dossier()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database, "Acme AI", "acme.com");

        var query = new CompanyResearchQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var lookup = (await query.ResolveCandidatesAsync("Acme AI")).ShouldHaveSingleItem();

        lookup.CompanyId.ShouldBe(companyId);
        lookup.DisplayName.ShouldBe("Acme AI");
        lookup.LatestDossier.ShouldBeNull();
    }

    [RequiresDockerFact]
    public async Task Resolving_by_name_is_case_insensitive()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        await SeedCompanyAsync(database, "Acme AI", "acme.com");

        var query = new CompanyResearchQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        (await query.ResolveCandidatesAsync("acme ai")).ShouldHaveSingleItem();
    }

    [RequiresDockerFact]
    public async Task Resolving_by_domain_or_bare_label_finds_the_same_company_once()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database, "Stripe", "stripe.com");

        var query = new CompanyResearchQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        // The full domain, a decorated URL and the bare registrable label all resolve to the one company —
        // and matching on both the name and the label at once still returns it once, not twice (catalogue §Company).
        (await query.ResolveCandidatesAsync("stripe.com")).ShouldHaveSingleItem().CompanyId.ShouldBe(companyId);
        (await query.ResolveCandidatesAsync("https://Stripe.com/careers")).ShouldHaveSingleItem().CompanyId.ShouldBe(companyId);
        (await query.ResolveCandidatesAsync("stripe")).ShouldHaveSingleItem().CompanyId.ShouldBe(companyId);
    }

    [RequiresDockerFact]
    public async Task A_bare_label_matching_two_companies_returns_both()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var comDomain = await SeedCompanyAsync(database, "Acme (com)", "acme.com", lastSeenAt: RunStart);
        var ioDomain = await SeedCompanyAsync(database, "Acme (io)", "acme.io", lastSeenAt: RunStart.AddDays(1));

        var query = new CompanyResearchQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var candidates = await query.ResolveCandidatesAsync("acme");

        // A genuine ambiguity: the bare label "acme" matches two distinct companies, returned most-recently-seen
        // first so the caller can offer both rather than silently picking one.
        candidates.Count.ShouldBe(2);
        candidates[0].CompanyId.ShouldBe(ioDomain);
        candidates[1].CompanyId.ShouldBe(comDomain);
    }

    [RequiresDockerFact]
    public async Task A_dossier_is_returned_with_its_claim_source_url_and_observed_date()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database, "Acme AI", "acme.com");
        var runId = await SeedRunAsync(database, RunStart);
        await SeedDossierAsync(
            database, companyId, runId, GeneratedAt,
            summary: "A short honest summary.",
            source: new SourceSeed(ResearchCategory.Funding, "https://acme.ai/press", "Press", GeneratedAt),
            claim: new ClaimSeed(ResearchCategory.Funding, "Raised a Series B.", IsWarning: false),
            unavailable: [ResearchCategory.Reviews]);

        var query = new CompanyResearchQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var lookup = (await query.ResolveCandidatesAsync("Acme AI")).ShouldHaveSingleItem();

        var dossier = lookup.LatestDossier.ShouldNotBeNull();
        dossier.Summary.ShouldBe("A short honest summary.");
        dossier.GeneratedAt.ShouldBe(GeneratedAt);
        var claim = dossier.Claims.ShouldHaveSingleItem();
        claim.Category.ShouldBe(ResearchCategory.Funding);
        claim.Claim.ShouldBe("Raised a Series B.");
        claim.SourceUrl.ShouldBe("https://acme.ai/press");
        claim.ObservedAt.ShouldBe(GeneratedAt);
        claim.IsWarning.ShouldBeFalse();
        dossier.CategoriesUnavailable.ShouldContain(ResearchCategory.Reviews);
    }

    [RequiresDockerFact]
    public async Task Only_the_latest_dossier_is_returned()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database, "Acme AI", "acme.com");
        var oldRun = await SeedRunAsync(database, RunStart.AddDays(-1));
        var newRun = await SeedRunAsync(database, RunStart);
        await SeedDossierAsync(
            database, companyId, oldRun, GeneratedAt.AddDays(-1), summary: "Old.",
            source: new SourceSeed(ResearchCategory.Funding, "https://acme.ai/old", "Old", GeneratedAt.AddDays(-1)),
            claim: new ClaimSeed(ResearchCategory.Funding, "Raised a Series A.", IsWarning: false),
            unavailable: []);
        await SeedDossierAsync(
            database, companyId, newRun, GeneratedAt, summary: "New.",
            source: new SourceSeed(ResearchCategory.Funding, "https://acme.ai/new", "New", GeneratedAt),
            claim: new ClaimSeed(ResearchCategory.Funding, "Raised a Series B.", IsWarning: false),
            unavailable: []);

        var query = new CompanyResearchQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var dossier = (await query.ResolveCandidatesAsync("Acme AI")).ShouldHaveSingleItem().LatestDossier.ShouldNotBeNull();

        dossier.Summary.ShouldBe("New.");
        dossier.Claims.ShouldHaveSingleItem().SourceUrl.ShouldBe("https://acme.ai/new");
    }

    [RequiresDockerFact]
    public async Task Warnings_are_returned_before_other_categories()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database, "Acme AI", "acme.com");
        var runId = await SeedRunAsync(database, RunStart);
        await SeedDossierAsync(
            database, companyId, runId, GeneratedAt, summary: "Mixed.",
            sources:
            [
                new SourceSeed(ResearchCategory.Funding, "https://acme.ai/press", "Press", GeneratedAt),
                new SourceSeed(ResearchCategory.Layoffs, "https://acme.ai/layoffs", "Layoffs", GeneratedAt),
            ],
            claims:
            [
                new ClaimSeed(ResearchCategory.Funding, "Raised a Series B.", IsWarning: false),
                new ClaimSeed(ResearchCategory.Layoffs, "Cut 10% of staff.", IsWarning: true),
            ],
            unavailable: []);

        var query = new CompanyResearchQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var dossier = (await query.ResolveCandidatesAsync("Acme AI")).ShouldHaveSingleItem().LatestDossier.ShouldNotBeNull();

        dossier.Claims[0].IsWarning.ShouldBeTrue();
        dossier.Claims[0].Category.ShouldBe(ResearchCategory.Layoffs);
    }

    [RequiresDockerFact]
    public async Task Latest_for_company_returns_the_dossier_by_id()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database, "Acme AI", "acme.com");
        var runId = await SeedRunAsync(database, RunStart);
        await SeedDossierAsync(
            database, companyId, runId, GeneratedAt, summary: "By id.",
            source: new SourceSeed(ResearchCategory.Funding, "https://acme.ai/press", "Press", GeneratedAt),
            claim: new ClaimSeed(ResearchCategory.Funding, "Raised a Series B.", IsWarning: false),
            unavailable: []);

        var query = new CompanyResearchQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        (await query.LatestForCompanyAsync(companyId)).ShouldNotBeNull().Summary.ShouldBe("By id.");
        (await query.LatestForCompanyAsync(Guid.CreateVersion7())).ShouldBeNull();
    }

    private static async Task<Guid> SeedCompanyAsync(
        TestDatabase database, string name, string domain, DateTimeOffset? lastSeenAt = null)
    {
        var companyId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        // The constructor sets LastSeenAt = firstSeenAt, so the seen instant is the first-seen argument.
        ctx.Add(new Company(
            companyId, CanonicalDomain.TryCreate(domain).Value, name, CompanySource.Curated, lastSeenAt ?? RunStart));
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

    private static Task SeedDossierAsync(
        TestDatabase database, Guid companyId, Guid runId, DateTimeOffset generatedAt,
        string summary, SourceSeed source, ClaimSeed claim, IReadOnlyList<ResearchCategory> unavailable) =>
        SeedDossierAsync(database, companyId, runId, generatedAt, summary, [source], [claim], unavailable);

    private static async Task SeedDossierAsync(
        TestDatabase database, Guid companyId, Guid runId, DateTimeOffset generatedAt,
        string summary, IReadOnlyList<SourceSeed> sources, IReadOnlyList<ClaimSeed> claims,
        IReadOnlyList<ResearchCategory> unavailable)
    {
        var sourceEntities = sources
            .Select(s => new ResearchSource(Guid.CreateVersion7(), s.Category, s.Url, s.Title, textLength: 100, s.ObservedAt))
            .ToList();
        var byCategory = sourceEntities.ToLookup(s => s.Category);
        var claimEntities = claims
            .Select(c => new ResearchClaim(Guid.CreateVersion7(), byCategory[c.Category].First(), c.Category, c.Claim, c.IsWarning))
            .ToList();

        var research = new CompanyResearch(
            Guid.CreateVersion7(), companyId, runId, summary, sourceEntities, claimEntities, unavailable,
            claimsDiscarded: 0, promptVersion: "research-v2", generatedAt);

        var repo = new ResearchRepository(database.CreateContext());
        repo.Add(research);
        await repo.SaveChangesAsync();
    }

    private sealed record SourceSeed(ResearchCategory Category, string Url, string Title, DateTimeOffset ObservedAt);

    private sealed record ClaimSeed(ResearchCategory Category, string Claim, bool IsWarning);
}
