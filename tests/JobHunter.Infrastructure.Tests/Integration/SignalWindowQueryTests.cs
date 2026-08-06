using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Preferences;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F7 T05 (SAD §6.1): the read side of the weekly refit — every captured signal at or after a cutoff,
/// projected into the <c>SignalFact</c> the pure <c>WeightFitter</c> consumes, newest first. The snapshotted
/// <c>job_facts</c> jsonb round-trips back into the <c>JobFacts</c> vocabulary without a join to <c>jobs</c>,
/// and signals before the cutoff are excluded so the 180-day window is a query bound, not a post-filter.
/// Requires Docker.
/// </summary>
public sealed class SignalWindowQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 6, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task It_loads_signals_at_or_after_the_cutoff_newest_first()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var older = await CaptureAsync(seed, SignalKind.Ignored, Now.AddDays(-30), Dimension.Country, "DE");
        var newer = await CaptureAsync(seed, SignalKind.Saved, Now.AddDays(-1), Dimension.Technology, "Kafka");

        var facts = await NewQuery(seed).LoadSince(Now.AddDays(-180));

        facts.Count.ShouldBe(2);
        facts[0].SignalId.ShouldBe(newer);   // newest first
        facts[1].SignalId.ShouldBe(older);
    }

    [RequiresDockerFact]
    public async Task Signals_before_the_cutoff_are_excluded()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        await CaptureAsync(seed, SignalKind.Ignored, Now.AddDays(-200), Dimension.Country, "DE");
        var inWindow = await CaptureAsync(seed, SignalKind.Saved, Now.AddDays(-10), Dimension.Country, "NL");

        var facts = await NewQuery(seed).LoadSince(Now.AddDays(-180));

        facts.ShouldHaveSingleItem().SignalId.ShouldBe(inWindow);
    }

    [RequiresDockerFact]
    public async Task Each_fact_carries_its_kind_weight_and_snapshotted_job_facts()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var id = await CaptureAsync(seed, SignalKind.Interview, Now.AddDays(-5), Dimension.Country, "NL");

        var fact = (await NewQuery(seed).LoadSince(Now.AddDays(-180))).ShouldHaveSingleItem();

        fact.SignalId.ShouldBe(id);
        fact.Kind.ShouldBe(SignalKind.Interview);
        fact.Weight.ShouldBe(SignalWeights.Default.WeightFor(SignalKind.Interview));
        fact.OccurredAt.ShouldBe(Now.AddDays(-5));
        fact.Facts.ValuesFor(Dimension.Country).ShouldContain("NL");
    }

    private static SignalWindowQuery NewQuery(Seed seed) =>
        new(new NpgsqlConnectionFactory(seed.Database.ConnectionString));

    private static async Task<Guid> CaptureAsync(
        Seed seed, SignalKind kind, DateTimeOffset occurredAt, Dimension dimension, string value)
    {
        var jobId = await SeedJobAsync(seed, occurredAt);
        var signalId = Guid.CreateVersion7();
        var facts = JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>> { [dimension] = [value] });
        var applicationId = kind is SignalKind.Applied or SignalKind.Interview or SignalKind.Offer or SignalKind.Rejected
            ? Guid.CreateVersion7()
            : (Guid?)null;
        var signal = Signal.Capture(signalId, jobId, applicationId, kind, facts, occurredAt, SignalWeights.Default);
        var repo = new SignalRepository(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        (await repo.TryCaptureAsync(signal)).ShouldBeTrue();
        return signalId;
    }

    private static async Task<Guid> SeedJobAsync(Seed seed, DateTimeOffset seenAt)
    {
        var rawPostingId = Guid.CreateVersion7();
        var jobId = Guid.CreateVersion7();

        await using var ctx = seed.Database.CreateContext();
        var payload = $"{{\"j\":\"{jobId:N}\"}}";
        ctx.Add(new RawPosting(rawPostingId, seed.SourceId, jobId.ToString("N"), ContentHash.Compute(payload), payload, 200, seenAt));
        var job = new Job(
            jobId, seed.CompanyId, rawPostingId, Fingerprint.TryCreate(jobId.ToString("N").PadRight(64, '0')).Value,
            fingerprintVersion: 1, "Staff SRE", normalisedTitle: "staff sre", description: "d",
            applyUrl: "https://acme.com/apply/1", LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            RemotePolicy.Hybrid, EmploymentType.FullTime, PostedAtGranularity.Day, firstSeenAt: seenAt, lastSeenAt: seenAt);
        ctx.Add(job);
        await ctx.SaveChangesAsync();
        return jobId;
    }

    private static async Task<Seed> SeedAsync()
    {
        var database = await TestDatabase.CreateAsync();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now));
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", Now));
        ctx.Add(new JobSource(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs"));
        await ctx.SaveChangesAsync();

        return new Seed(database, companyId, sourceId);
    }

    private sealed record Seed(TestDatabase Database, Guid CompanyId, Guid SourceId);
}
