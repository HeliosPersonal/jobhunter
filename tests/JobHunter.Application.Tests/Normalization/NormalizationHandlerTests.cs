using JobHunter.Application.Normalization;
using JobHunter.Application.Normalization.Providers;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Sources;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;

namespace JobHunter.Application.Tests.Normalization;

/// <summary>
/// T04: the normalisation handler turns one <see cref="RawPostingIngested"/> into a candidate job and
/// publishes <see cref="JobNormalized"/> carrying the fingerprint (event-catalog). It resolves the provider
/// by the binding's <see cref="AtsKind"/>, so adding a provider never touches this handler. It is idempotent
/// on the raw posting id: replaying the same ingest re-publishes the same candidate job id and fingerprint,
/// so the downstream insert conflicts rather than forking a second job. A missing required field, a malformed
/// payload, an unroutable provider, a vanished raw posting/company/binding — each is a recorded (logged)
/// normalisation failure that ends the message cleanly, never a throw that poisons the batch (AC-04).
/// Everything is stubbed: zero network, no clock-dependence beyond the injected <see cref="FakeClock"/>.
/// </summary>
public sealed class NormalizationHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FetchedAt = new(2026, 8, 3, 5, 0, 0, TimeSpan.Zero);

    private const string GreenhousePayload =
        """
        {
          "title": "Senior Platform Engineer",
          "absolute_url": "https://boards.greenhouse.io/acme/jobs/123",
          "location": { "name": "Berlin, Germany" },
          "content": "Build the platform."
        }
        """;

    private readonly IRawPostingReader _rawPostings = Substitute.For<IRawPostingReader>();
    private readonly IJobSourceRepository _sources = Substitute.For<IJobSourceRepository>();
    private readonly ICompanyRepository _companies = Substitute.For<ICompanyRepository>();
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
        new("Go", ["golang"]),
        new("Kubernetes", ["k8s"]),
    ];

    private readonly TechnologyTagger _tagger = new(new TechnologyVocabulary(VocabularyEntries));

    private NormalizationHandler CreateHandler() =>
        new(_rawPostings, _sources, _companies, _catalog, _tagger, _clock, NullLogger<NormalizationHandler>.Instance);

    private Task Handle(RawPostingIngested message) =>
        CreateHandler().Handle(message, _bus, CancellationToken.None);

    private (Guid RawPostingId, Guid SourceId, Guid CompanyId, Guid BindingId) SeedGreenhouse(string payload)
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

        return (rawPostingId, sourceId, companyId, bindingId);
    }

    private static Company Company(Guid companyId) =>
        new(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now);

    private static RawPostingIngested Ingest(Guid rawPostingId, Guid sourceId, Guid companyId) =>
        new(rawPostingId, sourceId, companyId, new string('a', 64), Now);

    [Fact]
    public async Task It_normalises_a_greenhouse_posting_and_publishes_job_normalized_with_a_fingerprint()
    {
        var (rawPostingId, sourceId, companyId, _) = SeedGreenhouse(GreenhousePayload);

        var published = new List<JobNormalized>();
        await _bus.PublishAsync(Arg.Do<JobNormalized>(m => published.Add(m)));

        await Handle(Ingest(rawPostingId, sourceId, companyId));

        var normalized = published.ShouldHaveSingleItem();
        normalized.RawPostingId.ShouldBe(rawPostingId);
        normalized.CompanyId.ShouldBe(companyId);
        normalized.Fingerprint.Length.ShouldBe(64);
        normalized.JobId.ShouldBe(CandidateJobId.For(rawPostingId));
        normalized.OccurredAt.ShouldBe(Now);
    }

    [Fact]
    public async Task Replaying_the_same_ingest_re_publishes_the_same_candidate_id_and_fingerprint()
    {
        var (rawPostingId, sourceId, companyId, _) = SeedGreenhouse(GreenhousePayload);

        var published = new List<JobNormalized>();
        await _bus.PublishAsync(Arg.Do<JobNormalized>(m => published.Add(m)));

        var handler = CreateHandler();
        await handler.Handle(Ingest(rawPostingId, sourceId, companyId), _bus, CancellationToken.None);
        await handler.Handle(Ingest(rawPostingId, sourceId, companyId), _bus, CancellationToken.None);

        published.Count.ShouldBe(2);
        published[0].JobId.ShouldBe(published[1].JobId);
        published[0].Fingerprint.ShouldBe(published[1].Fingerprint);
    }

    [Fact]
    public async Task A_posting_missing_a_required_field_is_recorded_not_thrown_and_publishes_nothing()
    {
        var (rawPostingId, sourceId, companyId, _) = SeedGreenhouse("{ \"location\": { \"name\": \"Berlin\" } }");

        await Should.NotThrowAsync(() => Handle(Ingest(rawPostingId, sourceId, companyId)));

        await _bus.DidNotReceive().PublishAsync(Arg.Any<JobNormalized>());
    }

    [Fact]
    public async Task A_malformed_payload_is_recorded_not_thrown_and_publishes_nothing()
    {
        var (rawPostingId, sourceId, companyId, _) = SeedGreenhouse("{ not json");

        await Should.NotThrowAsync(() => Handle(Ingest(rawPostingId, sourceId, companyId)));

        await _bus.DidNotReceive().PublishAsync(Arg.Any<JobNormalized>());
    }

    [Fact]
    public async Task A_raw_posting_deleted_in_flight_exits_cleanly()
    {
        var rawPostingId = Guid.CreateVersion7();
        _rawPostings.FindAsync(rawPostingId, Arg.Any<CancellationToken>()).Returns((RawPostingContent?)null);

        await Should.NotThrowAsync(() => Handle(Ingest(rawPostingId, Guid.CreateVersion7(), Guid.CreateVersion7())));

        await _bus.DidNotReceive().PublishAsync(Arg.Any<JobNormalized>());
    }

    [Fact]
    public async Task A_source_with_no_resolvable_binding_is_recorded_not_thrown()
    {
        var rawPostingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var companyId = Guid.CreateVersion7();
        _rawPostings.FindAsync(rawPostingId, Arg.Any<CancellationToken>())
            .Returns(new RawPostingContent(rawPostingId, sourceId, "ext-1", GreenhousePayload, FetchedAt, Now));
        _sources.FindAsync(sourceId, Arg.Any<CancellationToken>()).Returns((JobSource?)null);

        await Should.NotThrowAsync(() => Handle(Ingest(rawPostingId, sourceId, companyId)));

        await _bus.DidNotReceive().PublishAsync(Arg.Any<JobNormalized>());
    }

    [Fact]
    public async Task A_missing_company_is_recorded_not_thrown()
    {
        var (rawPostingId, sourceId, companyId, _) = SeedGreenhouse(GreenhousePayload);
        _companies.FindAsync(companyId, Arg.Any<CancellationToken>()).Returns((Company?)null);

        await Should.NotThrowAsync(() => Handle(Ingest(rawPostingId, sourceId, companyId)));

        await _bus.DidNotReceive().PublishAsync(Arg.Any<JobNormalized>());
    }
}
