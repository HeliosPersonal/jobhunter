using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Preferences;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F7 T03, the critical property: <c>job_facts</c> is snapshotted <em>at the moment of the action</em>, so a
/// later edit to the job can never rewrite what the Owner is recorded as having reacted to. The snapshot query
/// deliberately reads live state (<see cref="JobFactsSnapshotQuery"/> joins <c>jobs</c> each time); this test
/// captures a signal from that live read, mutates the job so a <em>re</em>-snapshot genuinely differs, and then
/// asserts the persisted signal's facts are byte-for-byte unchanged. Without the jsonb snapshot column — if the
/// signal joined to <c>jobs</c> at fitting time — this test would fail, which is exactly the regression it
/// guards. Requires Docker.
/// </summary>
public sealed class SignalSnapshotImmutabilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 6, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task Editing_the_job_after_capture_does_not_change_the_signals_facts()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var factory = new NpgsqlConnectionFactory(seed.Database.ConnectionString);
        var snapshots = new JobFactsSnapshotQuery(factory);
        var signals = new SignalRepository(factory);

        // Capture a signal from the live snapshot — this is the write path F5 and F6 both take.
        var atCapture = await snapshots.SnapshotAsync(seed.JobId);
        atCapture.ShouldNotBeNull();
        atCapture.ValuesFor(Dimension.RemotePolicy).ShouldBe([nameof(RemotePolicy.Hybrid)]);

        var signal = Signal.Capture(
            Guid.CreateVersion7(), seed.JobId, applicationId: null, SignalKind.Saved, atCapture, Now, SignalWeights.Default);
        (await signals.TryCaptureAsync(signal)).ShouldBeTrue();

        // Now edit the job the Owner reacted to: remote policy Hybrid -> Remote, and a new technology.
        await MutateJobAsync(seed);

        // A fresh snapshot reflects the edit — proving the mutation really changed the job's live facts.
        var afterEdit = await snapshots.SnapshotAsync(seed.JobId);
        afterEdit.ShouldNotBeNull();
        afterEdit.ValuesFor(Dimension.RemotePolicy).ShouldBe([nameof(RemotePolicy.Remote)]);
        afterEdit.ValuesFor(Dimension.Technology).ShouldContain("Rust");

        // But the persisted signal still carries the facts as they were at capture — nothing was rewritten.
        await using var read = seed.Database.CreateContext();
        var stored = await read.Set<Signal>().SingleAsync();
        stored.JobFacts.ValuesFor(Dimension.RemotePolicy).ShouldBe([nameof(RemotePolicy.Hybrid)]);
        stored.JobFacts.ValuesFor(Dimension.Technology).ShouldNotContain("Rust");
        stored.JobFacts.ShouldBe(atCapture);
    }

    private static async Task MutateJobAsync(Seed seed)
    {
        await using var connection = new NpgsqlConnection(seed.Database.ConnectionString);
        await connection.OpenAsync();

        await using (var update = connection.CreateCommand())
        {
            update.CommandText = "UPDATE jobs SET remote_policy = 'Remote' WHERE id = @id";
            update.Parameters.AddWithValue("id", seed.JobId);
            await update.ExecuteNonQueryAsync();
        }

        await using var tech = connection.CreateCommand();
        tech.CommandText =
            "INSERT INTO job_technologies (job_id, technology, matched_via) VALUES (@id, 'Rust', 'Description')";
        tech.Parameters.AddWithValue("id", seed.JobId);
        await tech.ExecuteNonQueryAsync();
    }

    private static async Task<Seed> SeedAsync()
    {
        var database = await TestDatabase.CreateAsync();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var rawPostingId = Guid.CreateVersion7();
        var jobId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now));
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", Now));
        ctx.Add(new JobSource(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs"));
        ctx.Add(new RawPosting(rawPostingId, sourceId, "job-1", ContentHash.Compute("{\"t\":\"x\"}"), "{\"t\":\"x\"}", 200, Now));
        var job = new Job(
            jobId, companyId, rawPostingId, Fingerprint.TryCreate(new string('a', 64)).Value,
            fingerprintVersion: 1, "Staff SRE", normalisedTitle: "staff sre", description: "d",
            applyUrl: "https://acme.com/apply/1", LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            RemotePolicy.Hybrid, EmploymentType.FullTime, PostedAtGranularity.Day, firstSeenAt: Now, lastSeenAt: Now);
        job.AddTechnology("Kafka", TechnologyMatch.Description);
        ctx.Add(job);
        await ctx.SaveChangesAsync();

        return new Seed(database, jobId);
    }

    private sealed record Seed(TestDatabase Database, Guid JobId);
}
