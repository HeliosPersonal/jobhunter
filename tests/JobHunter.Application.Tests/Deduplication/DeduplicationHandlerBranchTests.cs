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
/// F2 T06: the two recorded-failure arms of deduplication that the primary suite in
/// <see cref="DeduplicationHandlerTests"/> does not reach. When the resolved binding names an ATS kind for
/// which no normaliser is registered — a composition gap, not a data fault — the posting is unroutable. And a
/// payload that extracts cleanly yet lacks a required field (a title) fails the candidate-job factory rather
/// than the parser, a distinct arm from the malformed-JSON case the primary suite covers. Both are recorded
/// (logged) and end the message cleanly rather than throwing into the batch (AC-04). The first is proven with a
/// partial catalogue that omits the binding's provider; the second with a title-less-but-valid payload. Zero
/// network.
/// </summary>
public sealed class DeduplicationHandlerBranchTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FetchedAt = new(2026, 8, 3, 5, 0, 0, TimeSpan.Zero);

    private const string LeverPayload =
        """
        {
          "text": "Backend Engineer",
          "hostedUrl": "https://jobs.lever.co/acme/abc",
          "descriptionPlain": "We use Go and Postgres.",
          "categories": { "location": "Remote - EMEA", "commitment": "Full-time" }
        }
        """;

    private readonly IRawPostingReader _rawPostings = Substitute.For<IRawPostingReader>();
    private readonly IJobSourceRepository _sources = Substitute.For<IJobSourceRepository>();
    private readonly ICompanyRepository _companies = Substitute.For<ICompanyRepository>();
    private readonly IJobRepository _jobs = Substitute.For<IJobRepository>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly FakeClock _clock = new(Now);
    private readonly TechnologyTagger _tagger = new(new TechnologyVocabulary([new TechnologyEntry("Go", ["golang"])]));

    // A catalogue that registers only Greenhouse — so a Lever binding has no normaliser and is unroutable.
    private readonly IPostingNormalizerCatalog _partialCatalog =
        new PostingNormalizerCatalog(new IPostingNormalizer[] { new GreenhousePostingNormalizer() });

    private DeduplicationHandler CreateHandler() =>
        new(_rawPostings, _sources, _companies, _partialCatalog, _tagger, _jobs, _clock,
            NullLogger<DeduplicationHandler>.Instance);

    [Fact]
    public async Task A_binding_whose_ats_kind_has_no_registered_normaliser_is_unroutable_and_exits_cleanly()
    {
        var rawPostingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();

        _rawPostings.FindAsync(rawPostingId, Arg.Any<CancellationToken>())
            .Returns(new RawPostingContent(rawPostingId, sourceId, "ext-1", LeverPayload, FetchedAt, Now));
        _sources.FindAsync(sourceId, Arg.Any<CancellationToken>())
            .Returns(new JobSource(sourceId, companyId, bindingId, "https://api.lever.co/v0/postings/acme"));
        // The binding is Lever, which the partial catalogue does not cover.
        _companies.FindBindingAsync(bindingId, Arg.Any<CancellationToken>())
            .Returns(new AtsBinding(bindingId, companyId, AtsKind.Lever, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", Now));
        _companies.FindAsync(companyId, Arg.Any<CancellationToken>())
            .Returns(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now));

        var message = new JobNormalized(CandidateJobId.For(rawPostingId), rawPostingId, companyId, new string('a', 64), Now);

        await Should.NotThrowAsync(() => CreateHandler().Handle(message, _bus, CancellationToken.None));

        // Unroutable: no insert attempted, and neither discovery nor duplicate event published.
        await _jobs.DidNotReceive().InsertAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<JobDiscovered>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<JobDuplicateDetected>());
    }

    [Fact]
    public async Task A_payload_that_extracts_but_is_missing_a_required_field_is_recorded_and_exits_cleanly()
    {
        // Valid Greenhouse JSON with an apply URL but no title: extraction succeeds, so this is not the
        // malformed-payload arm — it is the candidate-job factory's missing-required-field failure. The message
        // ends cleanly without an insert.
        const string titleless =
            """
            {
              "absolute_url": "https://boards.greenhouse.io/acme/jobs/123",
              "location": { "name": "Berlin, Germany" },
              "content": "Build the platform."
            }
            """;

        var rawPostingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();

        // Full catalogue so the Greenhouse binding routes to a real normaliser that extracts successfully.
        var fullCatalog = new PostingNormalizerCatalog(new IPostingNormalizer[] { new GreenhousePostingNormalizer() });
        var handler = new DeduplicationHandler(
            _rawPostings, _sources, _companies, fullCatalog, _tagger, _jobs, _clock,
            NullLogger<DeduplicationHandler>.Instance);

        _rawPostings.FindAsync(rawPostingId, Arg.Any<CancellationToken>())
            .Returns(new RawPostingContent(rawPostingId, sourceId, "ext-1", titleless, FetchedAt, Now));
        _sources.FindAsync(sourceId, Arg.Any<CancellationToken>())
            .Returns(new JobSource(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs"));
        _companies.FindBindingAsync(bindingId, Arg.Any<CancellationToken>())
            .Returns(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", Now));
        _companies.FindAsync(companyId, Arg.Any<CancellationToken>())
            .Returns(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now));

        var message = new JobNormalized(CandidateJobId.For(rawPostingId), rawPostingId, companyId, new string('a', 64), Now);

        await Should.NotThrowAsync(() => handler.Handle(message, _bus, CancellationToken.None));

        await _jobs.DidNotReceive().InsertAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<JobDiscovered>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<JobDuplicateDetected>());
    }
}
