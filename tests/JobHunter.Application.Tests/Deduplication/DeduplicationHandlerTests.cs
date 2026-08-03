using JobHunter.Application.Deduplication;
using JobHunter.Application.Normalization;
using JobHunter.Application.Normalization.Providers;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Sources;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;

namespace JobHunter.Application.Tests.Deduplication;

/// <summary>
/// T06: the deduplication handler turns one <see cref="JobNormalized"/> into either the canonical job or an
/// alias on an existing one. It re-runs the deterministic normalisation over the stored payload, then does a
/// conflict-tolerant insert — the insert outcome is the whole concurrency design (invariant 2, ADR-F2-0001).
/// On an <see cref="JobInsertOutcome.Inserted"/> it publishes <see cref="JobDiscovered"/>; on a
/// <see cref="JobInsertOutcome.FingerprintConflict"/> it loads the winner, registers the posting as an alias
/// and publishes <see cref="JobDuplicateDetected"/>. It is idempotent on the fingerprint, and every
/// missing/failure path is a recorded no-throw that ends the message cleanly (AC-04). Zero network.
/// </summary>
public sealed class DeduplicationHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FetchedAt = new(2026, 8, 3, 5, 0, 0, TimeSpan.Zero);

    private const string GreenhousePayload =
        """
        {
          "title": "Senior Platform Engineer",
          "absolute_url": "https://boards.greenhouse.io/acme/jobs/123",
          "location": { "name": "Berlin, Germany" },
          "content": "Build the platform with Kubernetes."
        }
        """;

    private readonly IRawPostingReader _rawPostings = Substitute.For<IRawPostingReader>();
    private readonly IJobSourceRepository _sources = Substitute.For<IJobSourceRepository>();
    private readonly ICompanyRepository _companies = Substitute.For<ICompanyRepository>();
    private readonly IJobRepository _jobs = Substitute.For<IJobRepository>();
    private readonly IPostingNormalizerCatalog _catalog =
        new PostingNormalizerCatalog(new IPostingNormalizer[]
        {
            new GreenhousePostingNormalizer(),
            new LeverPostingNormalizer(),
            new AshbyPostingNormalizer(),
            new WorkablePostingNormalizer(),
            new CareersPagePostingNormalizer(),
        });
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly FakeClock _clock = new(Now);

    private static readonly TechnologyEntry[] VocabularyEntries =
    [
        new("Kubernetes", ["k8s"]),
    ];

    private readonly TechnologyTagger _tagger = new(new TechnologyVocabulary(VocabularyEntries));

    private DeduplicationHandler CreateHandler() =>
        new(_rawPostings, _sources, _companies, _catalog, _tagger, _jobs, _clock,
            NullLogger<DeduplicationHandler>.Instance);

    private Task Handle(JobNormalized message) =>
        CreateHandler().Handle(message, _bus, CancellationToken.None);

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
        _companies.FindAsync(companyId, Arg.Any<CancellationToken>())
            .Returns(Company(companyId));

        return (rawPostingId, sourceId, companyId);
    }

    private static Company Company(Guid companyId) =>
        new(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now);

    private static JobNormalized Normalized(Guid rawPostingId, Guid companyId)
    {
        var jobId = CandidateJobId.For(rawPostingId);
        return new JobNormalized(jobId, rawPostingId, companyId, new string('a', 64), Now);
    }

    [Fact]
    public async Task A_genuine_insert_publishes_job_discovered()
    {
        var (rawPostingId, _, companyId) = SeedGreenhouse(GreenhousePayload);
        _jobs.InsertAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(JobInsertOutcome.Inserted);

        var discovered = new List<JobDiscovered>();
        await _bus.PublishAsync(Arg.Do<JobDiscovered>(m => discovered.Add(m)));

        await Handle(Normalized(rawPostingId, companyId));

        var published = discovered.ShouldHaveSingleItem();
        published.JobId.ShouldBe(CandidateJobId.For(rawPostingId));
        published.CompanyId.ShouldBe(companyId);
        published.Title.ShouldBe("Senior Platform Engineer");
        published.OccurredAt.ShouldBe(Now);
        await _bus.DidNotReceive().PublishAsync(Arg.Any<JobDuplicateDetected>());
    }

    [Fact]
    public async Task The_inserted_job_carries_the_origin_posting_as_its_first_alias()
    {
        var (rawPostingId, sourceId, companyId) = SeedGreenhouse(GreenhousePayload);
        Job? inserted = null;
        _jobs.InsertAsync(Arg.Do<Job>(j => inserted = j), Arg.Any<CancellationToken>())
            .Returns(JobInsertOutcome.Inserted);

        await Handle(Normalized(rawPostingId, companyId));

        inserted.ShouldNotBeNull();
        var alias = inserted.Aliases.ShouldHaveSingleItem();
        alias.RawPostingId.ShouldBe(rawPostingId);
        alias.SourceId.ShouldBe(sourceId);
        inserted.FingerprintVersion.ShouldBe(FingerprintCalculator.Version);
    }

    [Fact]
    public async Task A_fingerprint_conflict_records_an_alias_and_publishes_duplicate_detected()
    {
        var (rawPostingId, sourceId, companyId) = SeedGreenhouse(GreenhousePayload);
        _jobs.InsertAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(JobInsertOutcome.FingerprintConflict);

        var canonical = ExistingCanonicalJob(companyId);
        _jobs.FindByFingerprintAsync(Arg.Any<Fingerprint>(), Arg.Any<CancellationToken>()).Returns(canonical);

        var duplicates = new List<JobDuplicateDetected>();
        await _bus.PublishAsync(Arg.Do<JobDuplicateDetected>(m => duplicates.Add(m)));

        await Handle(Normalized(rawPostingId, companyId));

        var published = duplicates.ShouldHaveSingleItem();
        published.CanonicalJobId.ShouldBe(canonical.Id);
        published.DuplicateRawPostingId.ShouldBe(rawPostingId);
        published.SourceId.ShouldBe(sourceId);

        // The posting is now an alias on the canonical job, and the write was persisted.
        canonical.Aliases.ShouldContain(a => a.RawPostingId == rawPostingId);
        await _jobs.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<JobDiscovered>());
    }

    [Fact]
    public async Task Two_consumers_racing_on_one_fingerprint_produce_one_job_and_two_aliases()
    {
        // First posting wins the insert; the second sees the conflict and aliases onto the winner.
        var firstRaw = Guid.CreateVersion7();
        var secondRaw = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();

        foreach (var raw in new[] { firstRaw, secondRaw })
        {
            _rawPostings.FindAsync(raw, Arg.Any<CancellationToken>())
                .Returns(new RawPostingContent(raw, sourceId, "ext", GreenhousePayload, FetchedAt, Now));
        }

        _sources.FindAsync(sourceId, Arg.Any<CancellationToken>())
            .Returns(new JobSource(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs"));
        _companies.FindBindingAsync(bindingId, Arg.Any<CancellationToken>())
            .Returns(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", Now));
        _companies.FindAsync(companyId, Arg.Any<CancellationToken>()).Returns(Company(companyId));

        Job? winner = null;
        _jobs.InsertAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(_ => JobInsertOutcome.Inserted, _ => JobInsertOutcome.FingerprintConflict);
        _jobs.When(j => j.InsertAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()))
            .Do(call => winner ??= call.Arg<Job>());
        _jobs.FindByFingerprintAsync(Arg.Any<Fingerprint>(), Arg.Any<CancellationToken>())
            .Returns(_ => winner);

        var handler = CreateHandler();
        await Should.NotThrowAsync(async () =>
        {
            await handler.Handle(Normalized(firstRaw, companyId), _bus, CancellationToken.None);
            await handler.Handle(Normalized(secondRaw, companyId), _bus, CancellationToken.None);
        });

        winner.ShouldNotBeNull();
        winner.Aliases.Select(a => a.RawPostingId).ShouldBe([firstRaw, secondRaw], ignoreOrder: true);
        await _bus.Received(1).PublishAsync(Arg.Any<JobDiscovered>());
        await _bus.Received(1).PublishAsync(Arg.Any<JobDuplicateDetected>());
    }

    [Fact]
    public async Task Replaying_the_same_conflict_re_registers_the_same_alias_without_forking()
    {
        var (rawPostingId, _, companyId) = SeedGreenhouse(GreenhousePayload);
        _jobs.InsertAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(JobInsertOutcome.FingerprintConflict);
        var canonical = ExistingCanonicalJob(companyId);
        _jobs.FindByFingerprintAsync(Arg.Any<Fingerprint>(), Arg.Any<CancellationToken>()).Returns(canonical);

        var handler = CreateHandler();
        await handler.Handle(Normalized(rawPostingId, companyId), _bus, CancellationToken.None);
        await handler.Handle(Normalized(rawPostingId, companyId), _bus, CancellationToken.None);

        // The alias is registered once per raw posting even after a replay (idempotent on the fingerprint).
        canonical.Aliases.Count(a => a.RawPostingId == rawPostingId).ShouldBe(1);
    }

    [Fact]
    public async Task A_conflict_whose_canonical_vanished_exits_cleanly()
    {
        var (rawPostingId, _, companyId) = SeedGreenhouse(GreenhousePayload);
        _jobs.InsertAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(JobInsertOutcome.FingerprintConflict);
        _jobs.FindByFingerprintAsync(Arg.Any<Fingerprint>(), Arg.Any<CancellationToken>()).Returns((Job?)null);

        await Should.NotThrowAsync(() => Handle(Normalized(rawPostingId, companyId)));

        await _bus.DidNotReceive().PublishAsync(Arg.Any<JobDuplicateDetected>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<JobDiscovered>());
    }

    [Fact]
    public async Task A_vanished_raw_posting_exits_cleanly()
    {
        var rawPostingId = Guid.CreateVersion7();
        _rawPostings.FindAsync(rawPostingId, Arg.Any<CancellationToken>()).Returns((RawPostingContent?)null);

        await Should.NotThrowAsync(() => Handle(Normalized(rawPostingId, Guid.CreateVersion7())));

        await _jobs.DidNotReceive().InsertAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<JobDiscovered>());
    }

    [Fact]
    public async Task A_source_with_no_resolvable_binding_exits_cleanly()
    {
        var rawPostingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var companyId = Guid.CreateVersion7();
        _rawPostings.FindAsync(rawPostingId, Arg.Any<CancellationToken>())
            .Returns(new RawPostingContent(rawPostingId, sourceId, "ext-1", GreenhousePayload, FetchedAt, Now));
        _sources.FindAsync(sourceId, Arg.Any<CancellationToken>()).Returns((JobSource?)null);

        await Should.NotThrowAsync(() => Handle(Normalized(rawPostingId, companyId)));

        await _jobs.DidNotReceive().InsertAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_missing_company_exits_cleanly()
    {
        var (rawPostingId, _, companyId) = SeedGreenhouse(GreenhousePayload);
        _companies.FindAsync(companyId, Arg.Any<CancellationToken>()).Returns((Company?)null);

        await Should.NotThrowAsync(() => Handle(Normalized(rawPostingId, companyId)));

        await _jobs.DidNotReceive().InsertAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unparseable_payload_exits_cleanly()
    {
        var (rawPostingId, _, companyId) = SeedGreenhouse("{ not json");

        await Should.NotThrowAsync(() => Handle(Normalized(rawPostingId, companyId)));

        await _jobs.DidNotReceive().InsertAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    private static Job ExistingCanonicalJob(Guid companyId)
    {
        var canonicalRaw = Guid.CreateVersion7();
        var location = LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]);
        var fingerprint = FingerprintCalculator.Compute("acme.com", "senior platform engineer", location);
        var job = new Job(
            Guid.CreateVersion7(),
            companyId,
            canonicalRaw,
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
        job.RegisterAlias(canonicalRaw, Guid.CreateVersion7(), Now, Now);
        return job;
    }
}
