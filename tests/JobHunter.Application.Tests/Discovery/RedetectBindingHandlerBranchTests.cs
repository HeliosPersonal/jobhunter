using JobHunter.Application.Discovery;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Sources;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Discovery;

/// <summary>
/// F1 T09 (AC-05): the migration-path arms of weekly re-detection that the primary suite in
/// <see cref="RedetectBindingHandlerTests"/> does not reach. A candidate whose company row has vanished is
/// skipped, not migrated onto nothing. A migration for a company that had a live binding but no operational
/// source, and one for a company with no live binding at all, both <em>create</em> a source rather than
/// re-pointing a missing one — so a re-detected-but-never-sourced company becomes fetchable. And the day-bucket
/// degenerates safely to zero when the bucket count is non-positive. Everything is stubbed, so these are
/// zero-network, zero-database unit tests.
/// </summary>
public sealed class RedetectBindingHandlerBranchTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 3, 30, 0, TimeSpan.Zero);

    private readonly IRedetectionQuery _due = Substitute.For<IRedetectionQuery>();
    private readonly ICompanyRepository _companies = Substitute.For<ICompanyRepository>();
    private readonly IJobSourceRepository _sources = Substitute.For<IJobSourceRepository>();
    private readonly IBindingDetector _detector = Substitute.For<IBindingDetector>();
    private readonly SequentialIdGenerator _ids = new();
    private readonly FakeClock _clock = new(Now);

    private RedetectBindingHandler CreateHandler() =>
        new(_due, _companies, _sources, _detector, _ids, _clock,
            NullLogger<RedetectBindingHandler>.Instance);

    private Task Handle(DiscoveryOptions? options = null) =>
        CreateHandler().Handle(new RedetectBindingsDue(Now), options ?? new DiscoveryOptions(), CancellationToken.None);

    private static Company Company(Guid id) =>
        new(id, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now);

    private static AtsBinding Binding(Guid id, Guid companyId, AtsKind kind, string token) =>
        new(id, companyId, kind, token, BindingConfidence.TryCreate(0.95m).Value, "{\"e\":true}", Now.AddDays(-30));

    private void SeedCandidate(Guid companyId) =>
        _due.DueCandidatesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new RedetectionCandidate(companyId)]);

    [Fact]
    public async Task A_candidate_whose_company_vanished_is_skipped_without_detecting_or_migrating()
    {
        var companyId = Guid.CreateVersion7();
        SeedCandidate(companyId);
        _companies.FindAsync(companyId, Arg.Any<CancellationToken>()).Returns((Company?)null);

        await Handle();

        // A vanished company is skipped before the probe — no detection, no binding change.
        await _detector.DidNotReceive().DetectAsync(Arg.Any<Company>(), Arg.Any<CancellationToken>());
        await _companies.DidNotReceive().AddBindingAsync(Arg.Any<AtsBinding>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_migration_for_a_live_binding_with_no_source_creates_a_source_pointed_at_the_new_binding()
    {
        var companyId = Guid.CreateVersion7();
        var oldBindingId = Guid.CreateVersion7();
        SeedCandidate(companyId);
        _companies.FindAsync(companyId, Arg.Any<CancellationToken>()).Returns(Company(companyId));

        var oldBinding = Binding(oldBindingId, companyId, AtsKind.Greenhouse, "acme");
        _companies.LiveBindingsAsync(companyId, Arg.Any<CancellationToken>()).Returns([oldBinding]);

        var detected = Binding(Guid.CreateVersion7(), companyId, AtsKind.Lever, "acme-lever");
        _detector.DetectAsync(Arg.Any<Company>(), Arg.Any<CancellationToken>())
            .Returns(new BindingDetectionResult(BindingDetectionStatus.Bound, detected));

        // No operational source exists for any live binding — FindByBindingAsync returns null throughout.
        _sources.FindByBindingAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((JobSource?)null);

        AtsBinding? recorded = null;
        await _companies.AddBindingAsync(Arg.Do<AtsBinding>(b => recorded = b), Arg.Any<CancellationToken>());
        JobSource? created = null;
        await _sources.AddAsync(Arg.Do<JobSource>(s => created = s), Arg.Any<CancellationToken>());

        await Handle();

        oldBinding.IsLive.ShouldBeFalse();
        recorded.ShouldNotBeNull();
        // A source is created (not re-pointed) and it targets the freshly recorded binding for the new provider.
        created.ShouldNotBeNull();
        created.CompanyId.ShouldBe(companyId);
        created.BindingId.ShouldBe(recorded.Id);
        created.EndpointUrl.ShouldBe(AtsEndpoint.For(AtsKind.Lever, "acme-lever"));
        await _sources.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_migration_for_a_company_with_no_live_binding_at_all_creates_a_source()
    {
        var companyId = Guid.CreateVersion7();
        SeedCandidate(companyId);
        _companies.FindAsync(companyId, Arg.Any<CancellationToken>()).Returns(Company(companyId));

        // No live binding: detection binds a provider for a company that was re-detected but never sourced.
        _companies.LiveBindingsAsync(companyId, Arg.Any<CancellationToken>()).Returns([]);

        var detected = Binding(Guid.CreateVersion7(), companyId, AtsKind.Ashby, "acme-ashby");
        _detector.DetectAsync(Arg.Any<Company>(), Arg.Any<CancellationToken>())
            .Returns(new BindingDetectionResult(BindingDetectionStatus.Bound, detected));

        AtsBinding? recorded = null;
        await _companies.AddBindingAsync(Arg.Do<AtsBinding>(b => recorded = b), Arg.Any<CancellationToken>());
        JobSource? created = null;
        await _sources.AddAsync(Arg.Do<JobSource>(s => created = s), Arg.Any<CancellationToken>());

        await Handle();

        // With no live bindings the source lookup is skipped entirely and a new source is created.
        await _sources.DidNotReceive().FindByBindingAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        recorded.ShouldNotBeNull();
        created.ShouldNotBeNull();
        created.CompanyId.ShouldBe(companyId);
        created.BindingId.ShouldBe(recorded.Id);
    }

    [Fact]
    public async Task A_non_positive_bucket_count_degenerates_the_day_bucket_to_zero()
    {
        _due.DueCandidatesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await Handle(new DiscoveryOptions { RedetectionBuckets = 0 });

        // DayBucket guards against a zero divisor: the bucket is 0 and the count is passed through verbatim.
        await _due.Received(1).DueCandidatesAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Is(0), Arg.Is(0), Arg.Any<CancellationToken>());
    }
}
