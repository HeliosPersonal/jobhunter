using JobHunter.Domain.Applications;
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
using App = JobHunter.Domain.Applications.Application;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F7 T03 (done-when 5): the backfill read surfaces terminal application outcomes that have no captured signal
/// yet, oldest first, so the service can replay them. It returns the outcome once for each outcome transition
/// that lacks a matching <c>(job_id, kind, occurred_at)</c> signal — the anti-join is what makes a run over a
/// fully migrated history yield nothing, so the query, not merely the capture, is idempotent. Non-outcome
/// transitions (New, Saved, a posting-closed self-transition) are never returned — they are not outcomes and
/// mint no signal. Requires Docker.
/// </summary>
public sealed class BackfillableOutcomeQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 6, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task It_streams_outcome_transitions_without_a_signal_oldest_first()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        // An Applied outcome (older) and an Interview outcome (newer), neither with a signal yet.
        var applied = await SeedApplicationAsync(seed, ApplicationStatus.Applied, Now.AddDays(-3));
        var interview = await SeedApplicationAsync(seed, ApplicationStatus.Interview, Now.AddDays(-1));

        var rows = await DrainAsync(seed, Now.AddDays(-10));

        rows.Count.ShouldBe(2);
        rows[0].JobId.ShouldBe(applied.JobId);       // oldest first
        rows[0].ToStatus.ShouldBe(ApplicationStatus.Applied);
        rows[0].ApplicationId.ShouldBe(applied.ApplicationId);
        rows[0].OccurredAt.ShouldBe(Now.AddDays(-3));
        rows[1].JobId.ShouldBe(interview.JobId);
        rows[1].ToStatus.ShouldBe(ApplicationStatus.Interview);
    }

    [RequiresDockerFact]
    public async Task An_outcome_that_already_has_a_signal_is_excluded()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var applied = await SeedApplicationAsync(seed, ApplicationStatus.Applied, Now.AddDays(-3));
        var interview = await SeedApplicationAsync(seed, ApplicationStatus.Interview, Now.AddDays(-1));

        // The Applied outcome already produced its signal — only the Interview one remains to backfill.
        await CaptureSignalAsync(seed, applied.JobId, applied.ApplicationId, SignalKind.Applied, Now.AddDays(-3));

        var rows = await DrainAsync(seed, Now.AddDays(-10));

        rows.ShouldHaveSingleItem().JobId.ShouldBe(interview.JobId);
    }

    [RequiresDockerFact]
    public async Task Non_outcome_transitions_are_never_returned()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        // A Saved application: its transitions are New (creating) and Saved — neither is an outcome kind.
        await SeedApplicationAsync(seed, ApplicationStatus.Saved, Now.AddDays(-2));

        var rows = await DrainAsync(seed, Now.AddDays(-10));

        rows.ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task Outcomes_before_the_cutoff_are_excluded()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        await SeedApplicationAsync(seed, ApplicationStatus.Applied, Now.AddDays(-10));
        var recent = await SeedApplicationAsync(seed, ApplicationStatus.Rejected, Now.AddDays(-1));

        var rows = await DrainAsync(seed, Now.AddDays(-5));

        rows.ShouldHaveSingleItem().JobId.ShouldBe(recent.JobId);
    }

    private static BackfillableOutcomeQuery NewQuery(Seed seed) =>
        new(new NpgsqlConnectionFactory(seed.Database.ConnectionString));

    private static async Task<List<BackfillableOutcome>> DrainAsync(Seed seed, DateTimeOffset from)
    {
        var results = new List<BackfillableOutcome>();
        await foreach (var row in NewQuery(seed).StreamAsync(from))
        {
            results.Add(row);
        }

        return results;
    }

    private static async Task CaptureSignalAsync(
        Seed seed, Guid jobId, Guid applicationId, SignalKind kind, DateTimeOffset occurredAt)
    {
        var facts = JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
        {
            [Dimension.RemotePolicy] = [nameof(RemotePolicy.Hybrid)],
        });
        var signal = Signal.Capture(
            Guid.CreateVersion7(), jobId, applicationId, kind, facts, occurredAt, SignalWeights.Default);
        var repo = new SignalRepository(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        (await repo.TryCaptureAsync(signal)).ShouldBeTrue();
    }

    private static async Task<(Guid JobId, Guid ApplicationId)> SeedApplicationAsync(
        Seed seed, ApplicationStatus outcome, DateTimeOffset occurredAt)
    {
        var rawPostingId = Guid.CreateVersion7();
        var jobId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var policy = ReminderPolicy.Default;

        await using var ctx = seed.Database.CreateContext();
        var payload = $"{{\"j\":\"{jobId:N}\"}}";
        ctx.Add(new RawPosting(rawPostingId, seed.SourceId, jobId.ToString("N"), ContentHash.Compute(payload), payload, 200, occurredAt));
        var job = new Job(
            jobId, seed.CompanyId, rawPostingId, Fingerprint.TryCreate(jobId.ToString("N").PadRight(64, '0')).Value,
            fingerprintVersion: 1, "Staff SRE", normalisedTitle: "staff sre", description: "d",
            applyUrl: "https://acme.com/apply/1", LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            RemotePolicy.Hybrid, EmploymentType.FullTime, PostedAtGranularity.Day, firstSeenAt: occurredAt, lastSeenAt: occurredAt);
        ctx.Add(job);

        var application = App.Create(applicationId, jobId, occurredAt, TransitionSource.Telegram);
        // New reaches Saved/Applied/Interview/Rejected directly (TransitionRules) — the statuses these tests
        // seed — so a single move records the target transition as the application's latest.
        application.ChangeStatus(outcome, TransitionSource.Telegram, occurredAt, policy).IsSuccess.ShouldBeTrue();
        ctx.Add(application);
        await ctx.SaveChangesAsync();

        return (jobId, applicationId);
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
