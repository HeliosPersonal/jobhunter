using System.Reflection;
using JobHunter.Application.Deduplication;
using JobHunter.Application.Normalization;
using JobHunter.Application.Normalization.Providers;
using JobHunter.Application.Reprocessing;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Sources;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Reprocessing;

/// <summary>
/// T09 / AC-09 / QG-3: the reprocessing service re-runs normalisation and deduplication over stored raw
/// payloads with zero network. A job whose recomputed fingerprint is unchanged keeps its id — enrichments and
/// matches stay attached — so the service does nothing to it. A job whose fingerprint changed is not mutated
/// in place; a new job is created for the new fingerprint and the old one is recorded as
/// <see cref="JobStatus.Superseded"/> pointing at its successor, never orphaned. Everything is stubbed: the
/// only I/O port the service holds is the stored-payload reader, which is what makes "zero network" structural.
/// </summary>
public sealed class ReprocessingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FetchedAt = new(2026, 8, 3, 5, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset From = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string GreenhousePayload =
        """
        {
          "title": "Senior Platform Engineer",
          "absolute_url": "https://boards.greenhouse.io/acme/jobs/123",
          "location": { "name": "Berlin, Germany" },
          "content": "Build the platform with Kubernetes."
        }
        """;

    private readonly IReprocessableJobsQuery _reprocessable = Substitute.For<IReprocessableJobsQuery>();
    private readonly IRawPostingReader _rawPostings = Substitute.For<IRawPostingReader>();
    private readonly IJobSourceRepository _sources = Substitute.For<IJobSourceRepository>();
    private readonly ICompanyRepository _companies = Substitute.For<ICompanyRepository>();
    private readonly IJobRepository _jobs = Substitute.For<IJobRepository>();
    private readonly IIdGenerator _ids = new SequentialIdGenerator();
    private readonly FakeClock _clock = new(Now);

    private readonly PostingNormalizerCatalog _catalog =
        new(new IPostingNormalizer[]
        {
            new GreenhousePostingNormalizer(),
            new LeverPostingNormalizer(),
            new AshbyPostingNormalizer(),
            new WorkablePostingNormalizer(),
            new CareersPagePostingNormalizer(),
        });

    private readonly TechnologyTagger _tagger =
        new(new TechnologyVocabulary([new TechnologyEntry("Kubernetes", ["k8s"])]));

    private ReprocessingService CreateService() =>
        new(_reprocessable, _rawPostings, _sources, _companies, _catalog, _tagger, _jobs, _ids, _clock,
            NullLogger<ReprocessingService>.Instance);

    private (Guid RawPostingId, Guid SourceId, Guid CompanyId) SeedGreenhouse(string payload)
    {
        var rawPostingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();

        _rawPostings.FindAsync(rawPostingId, Arg.Any<CancellationToken>())
            .Returns(new RawPostingContent(rawPostingId, sourceId, "ext-1", payload, FetchedAt, Now));
        _sources.FindAsync(sourceId, Arg.Any<CancellationToken>())
            .Returns(new JobSource(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs"));
        _companies.FindBindingAsync(bindingId, Arg.Any<CancellationToken>())
            .Returns(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", Now));
        _companies.FindAsync(companyId, Arg.Any<CancellationToken>()).Returns(Company(companyId));

        return (rawPostingId, sourceId, companyId);
    }

    private static Company Company(Guid companyId) =>
        new(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now);

    private void GivenReprocessable(params ReprocessableJob[] jobs) =>
        _reprocessable.StreamAsync(From, Arg.Any<CancellationToken>()).Returns(Stream(jobs));

    private static async IAsyncEnumerable<ReprocessableJob> Stream(ReprocessableJob[] jobs)
    {
        foreach (var job in jobs)
        {
            yield return job;
        }

        await Task.CompletedTask;
    }

    [Fact]
    public void The_service_holds_no_network_port_so_reprocessing_cannot_fetch()
    {
        // "Zero network" is structural: the reprocessing service depends on the stored-payload reader, never
        // on IJobSource. No constructor parameter can reach a provider, so a run physically cannot fetch.
        var parameters = typeof(ReprocessingService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType);

        parameters.ShouldNotContain(typeof(IJobSource));
    }

    [Fact]
    public async Task An_unchanged_fingerprint_preserves_the_job_and_writes_nothing()
    {
        var (rawPostingId, _, companyId) = SeedGreenhouse(GreenhousePayload);

        // The current fingerprint equals what re-normalising the stored payload produces.
        var current = FingerprintForStoredPayload(companyId, rawPostingId);
        GivenReprocessable(new ReprocessableJob(
            Guid.CreateVersion7(), companyId, rawPostingId, current));

        var report = await CreateService().ReprocessAsync(From, CancellationToken.None);

        report.Examined.ShouldBe(1);
        report.Unchanged.ShouldBe(1);
        report.Superseded.ShouldBe(0);
        // No new job, no supersession — the identity (and its enrichments/matches) is untouched.
        await _jobs.DidNotReceive().InsertAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
        await _jobs.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_changed_fingerprint_creates_a_new_job_and_supersedes_the_old_one()
    {
        var (rawPostingId, _, companyId) = SeedGreenhouse(GreenhousePayload);
        var oldJobId = Guid.CreateVersion7();

        // The stored fingerprint differs from what the improved rule now computes.
        GivenReprocessable(new ReprocessableJob(
            oldJobId, companyId, rawPostingId, new string('f', 64)));

        Job? inserted = null;
        _jobs.InsertAsync(Arg.Do<Job>(j => inserted = j), Arg.Any<CancellationToken>())
            .Returns(JobInsertOutcome.Inserted);
        _jobs.FindByFingerprintAsync(Arg.Any<Fingerprint>(), Arg.Any<CancellationToken>())
            .Returns(_ => inserted);

        var oldJob = ExistingJob(oldJobId, companyId, rawPostingId);
        _jobs.FindAsync(oldJobId, Arg.Any<CancellationToken>()).Returns(oldJob);

        var report = await CreateService().ReprocessAsync(From, CancellationToken.None);

        report.Examined.ShouldBe(1);
        report.Unchanged.ShouldBe(0);
        report.Superseded.ShouldBe(1);

        inserted.ShouldNotBeNull();
        inserted.Id.ShouldNotBe(oldJobId);

        // The old job is retired in favour of the new one, not deleted or orphaned.
        oldJob.Status.ShouldBe(JobStatus.Superseded);
        oldJob.SupersededBy.ShouldBe(inserted.Id);
        await _jobs.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_changed_fingerprint_that_collides_with_an_existing_job_supersedes_onto_that_job()
    {
        var (rawPostingId, _, companyId) = SeedGreenhouse(GreenhousePayload);
        var oldJobId = Guid.CreateVersion7();
        GivenReprocessable(new ReprocessableJob(
            oldJobId, companyId, rawPostingId, new string('f', 64)));

        // The new fingerprint already belongs to another job (a genuine merge under the improved rule).
        var winner = ExistingJob(Guid.CreateVersion7(), companyId, Guid.CreateVersion7());
        _jobs.InsertAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(JobInsertOutcome.FingerprintConflict);
        _jobs.FindByFingerprintAsync(Arg.Any<Fingerprint>(), Arg.Any<CancellationToken>()).Returns(winner);

        var oldJob = ExistingJob(oldJobId, companyId, rawPostingId);
        _jobs.FindAsync(oldJobId, Arg.Any<CancellationToken>()).Returns(oldJob);

        var report = await CreateService().ReprocessAsync(From, CancellationToken.None);

        report.Superseded.ShouldBe(1);
        oldJob.Status.ShouldBe(JobStatus.Superseded);
        oldJob.SupersededBy.ShouldBe(winner.Id);
    }

    [Fact]
    public async Task A_vanished_origin_payload_is_recorded_as_a_failure_and_skipped()
    {
        var companyId = Guid.CreateVersion7();
        var rawPostingId = Guid.CreateVersion7();
        _rawPostings.FindAsync(rawPostingId, Arg.Any<CancellationToken>()).Returns((RawPostingContent?)null);
        GivenReprocessable(new ReprocessableJob(
            Guid.CreateVersion7(), companyId, rawPostingId, new string('a', 64)));

        var report = await CreateService().ReprocessAsync(From, CancellationToken.None);

        report.Examined.ShouldBe(1);
        report.Failed.ShouldBe(1);
        await _jobs.DidNotReceive().InsertAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unparseable_stored_payload_is_recorded_as_a_failure_and_never_throws()
    {
        var (rawPostingId, _, companyId) = SeedGreenhouse("{ not json");
        GivenReprocessable(new ReprocessableJob(
            Guid.CreateVersion7(), companyId, rawPostingId, new string('a', 64)));

        ReprocessingReport? report = null;
        await Should.NotThrowAsync(async () =>
            report = await CreateService().ReprocessAsync(From, CancellationToken.None));

        report.ShouldNotBeNull();
        report.Failed.ShouldBe(1);
        await _jobs.DidNotReceive().InsertAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_empty_history_reprocesses_nothing_cleanly()
    {
        GivenReprocessable();

        var report = await CreateService().ReprocessAsync(From, CancellationToken.None);

        report.Examined.ShouldBe(0);
        report.Unchanged.ShouldBe(0);
        report.Superseded.ShouldBe(0);
        report.Failed.ShouldBe(0);
    }

    private string FingerprintForStoredPayload(Guid companyId, Guid rawPostingId)
    {
        var content = _rawPostings.FindAsync(rawPostingId, CancellationToken.None).Result!;
        var extraction = _catalog.For(AtsKind.Greenhouse)!.Extract(content.Payload);
        var context = new NormalizationContext(
            companyId, rawPostingId, content.SourceId, "acme.com", content.FetchedAt, content.LastSeenAt);
        var candidate = CandidateJobFactory.Create(Guid.CreateVersion7(), extraction.Value, context, _tagger);
        return candidate.Value.Fingerprint.Value;
    }

    private static Job ExistingJob(Guid id, Guid companyId, Guid originRawPostingId)
    {
        var location = LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]);
        var fingerprint = FingerprintCalculator.Compute("acme.com", "senior platform engineer", location);
        var job = new Job(
            id,
            companyId,
            originRawPostingId,
            fingerprint,
            FingerprintCalculator.Version,
            "Senior Platform Engineer",
            "senior platform engineer",
            "Build the platform.",
            "https://boards.greenhouse.io/acme/jobs/123",
            location,
            RemotePolicy.Onsite,
            EmploymentType.FullTime,
            PostedAtGranularity.Day,
            Now,
            Now);
        job.RegisterAlias(originRawPostingId, Guid.CreateVersion7(), Now, Now);
        return job;
    }
}
