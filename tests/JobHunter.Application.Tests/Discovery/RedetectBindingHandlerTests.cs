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
/// T09 (AC-05): weekly binding re-detection and ATS migration. When a company's board has changed provider,
/// re-detection retires the old binding (never deletes it), records the new one, and re-points the
/// operational source — keeping its company id so every posting already discovered stays attached to the
/// same company. A probe that re-confirms the same provider only refreshes the binding; an ambiguous or
/// empty probe leaves the company untouched, so a board that legitimately has no openings is never retired
/// on that basis. Everything is stubbed, so these are zero-network, zero-database unit tests.
/// </summary>
public sealed class RedetectBindingHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 3, 30, 0, TimeSpan.Zero);

    private readonly IRedetectionQuery _due = Substitute.For<IRedetectionQuery>();
    private readonly ICompanyRepository _companies = Substitute.For<ICompanyRepository>();
    private readonly IJobSourceRepository _sources = Substitute.For<IJobSourceRepository>();
    private readonly IBindingDetector _detector = Substitute.For<IBindingDetector>();
    private readonly SequentialIdGenerator _ids = new();
    private readonly FakeClock _clock = new(Now);
    private readonly DiscoveryOptions _options = new();

    private RedetectBindingHandler CreateHandler() =>
        new(_due, _companies, _sources, _detector, _ids, _clock,
            NullLogger<RedetectBindingHandler>.Instance);

    private Task Handle() =>
        CreateHandler().Handle(new RedetectBindingsDue(Now), _options, CancellationToken.None);

    private static Company Company(Guid id) =>
        new(id, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now);

    private static AtsBinding Binding(Guid id, Guid companyId, AtsKind kind, string token, DateTimeOffset detectedAt) =>
        new(id, companyId, kind, token, BindingConfidence.TryCreate(0.95m).Value, "{\"old\":true}", detectedAt);

    private void SeedCandidate(Guid companyId) =>
        _due.DueCandidatesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new RedetectionCandidate(companyId)]);

    [Fact]
    public async Task A_migrated_company_retires_the_old_binding_records_the_new_one_and_keeps_the_source_company()
    {
        var companyId = Guid.CreateVersion7();
        var oldBindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        SeedCandidate(companyId);

        _companies.FindAsync(companyId, Arg.Any<CancellationToken>()).Returns(Company(companyId));

        var oldBinding = Binding(oldBindingId, companyId, AtsKind.Greenhouse, "acme", Now.AddDays(-30));
        _companies.LiveBindingsAsync(companyId, Arg.Any<CancellationToken>()).Returns([oldBinding]);

        // Detection now finds the company's jobs on Lever — a genuine provider change.
        var detected = Binding(Guid.CreateVersion7(), companyId, AtsKind.Lever, "acme-lever", Now);
        _detector.DetectAsync(Arg.Any<Company>(), Arg.Any<CancellationToken>())
            .Returns(new BindingDetectionResult(BindingDetectionStatus.Bound, detected));

        var source = new JobSource(sourceId, companyId, oldBindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs");
        _sources.FindByBindingAsync(oldBindingId, Arg.Any<CancellationToken>()).Returns(source);

        AtsBinding? recorded = null;
        await _companies.AddBindingAsync(Arg.Do<AtsBinding>(b => recorded = b), Arg.Any<CancellationToken>());

        await Handle();

        // Old binding retired, never deleted.
        oldBinding.RetiredAt.ShouldBe(Now);
        oldBinding.IsLive.ShouldBeFalse();

        // New binding recorded for the new provider.
        recorded.ShouldNotBeNull();
        recorded!.AtsKind.ShouldBe(AtsKind.Lever);
        recorded.BoardToken.ShouldBe("acme-lever");

        // The source is re-pointed at the new binding but keeps its company — jobs stay attached.
        source.BindingId.ShouldBe(recorded.Id);
        source.CompanyId.ShouldBe(companyId);
        source.EndpointUrl.ShouldBe(AtsEndpoint.For(AtsKind.Lever, "acme-lever"));

        await _companies.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _sources.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Re_detecting_the_same_provider_refreshes_the_binding_without_migrating()
    {
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        SeedCandidate(companyId);

        _companies.FindAsync(companyId, Arg.Any<CancellationToken>()).Returns(Company(companyId));

        var current = Binding(bindingId, companyId, AtsKind.Greenhouse, "acme", Now.AddDays(-30));
        _companies.LiveBindingsAsync(companyId, Arg.Any<CancellationToken>()).Returns([current]);

        // Detection re-confirms the same provider and token, with fresh evidence.
        var detected = new AtsBinding(
            Guid.CreateVersion7(), companyId, AtsKind.Greenhouse, "acme",
            BindingConfidence.TryCreate(0.95m).Value, "{\"fresh\":true}", Now);
        _detector.DetectAsync(Arg.Any<Company>(), Arg.Any<CancellationToken>())
            .Returns(new BindingDetectionResult(BindingDetectionStatus.Bound, detected));

        await Handle();

        current.IsLive.ShouldBeTrue();
        current.DetectedAt.ShouldBe(Now);
        current.Evidence.ShouldBe("{\"fresh\":true}");
        await _companies.DidNotReceive().AddBindingAsync(Arg.Any<AtsBinding>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(BindingDetectionStatus.NoBoardFound)]
    [InlineData(BindingDetectionStatus.Ambiguous)]
    public async Task An_empty_or_ambiguous_probe_leaves_the_company_untouched(BindingDetectionStatus status)
    {
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        SeedCandidate(companyId);

        _companies.FindAsync(companyId, Arg.Any<CancellationToken>()).Returns(Company(companyId));
        var current = Binding(bindingId, companyId, AtsKind.Greenhouse, "acme", Now.AddDays(-30));
        _companies.LiveBindingsAsync(companyId, Arg.Any<CancellationToken>()).Returns([current]);

        _detector.DetectAsync(Arg.Any<Company>(), Arg.Any<CancellationToken>())
            .Returns(new BindingDetectionResult(status, null));

        await Handle();

        current.IsLive.ShouldBeTrue();
        current.RetiredAt.ShouldBeNull();
        await _companies.DidNotReceive().AddBindingAsync(Arg.Any<AtsBinding>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_cutoff_empty_cycles_and_the_day_bucket_are_passed_to_the_query()
    {
        _due.DueCandidatesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await Handle();

        var expectedBucket = (Now.DayOfYear - 1) % _options.RedetectionBuckets;
        await _due.Received(1).DueCandidatesAsync(
            Now - _options.BindingMaxAge,
            _options.RedetectionEmptyCycles,
            expectedBucket,
            _options.RedetectionBuckets,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_run_with_no_candidates_saves_nothing()
    {
        _due.DueCandidatesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await Handle();

        await _companies.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _sources.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _detector.DidNotReceive().DetectAsync(Arg.Any<Company>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Re_running_the_same_window_twice_migrates_once_because_the_binding_is_no_longer_a_candidate()
    {
        // First run migrates; on the second run the company's live binding already matches the detected
        // provider, so it is re-confirmed rather than migrated a second time (idempotent on the outcome).
        var companyId = Guid.CreateVersion7();
        var oldBindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        SeedCandidate(companyId);
        _companies.FindAsync(companyId, Arg.Any<CancellationToken>()).Returns(Company(companyId));

        var detected = Binding(Guid.CreateVersion7(), companyId, AtsKind.Lever, "acme-lever", Now);
        _detector.DetectAsync(Arg.Any<Company>(), Arg.Any<CancellationToken>())
            .Returns(new BindingDetectionResult(BindingDetectionStatus.Bound, detected));

        var oldBinding = Binding(oldBindingId, companyId, AtsKind.Greenhouse, "acme", Now.AddDays(-30));
        var source = new JobSource(sourceId, companyId, oldBindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs");
        _sources.FindByBindingAsync(oldBindingId, Arg.Any<CancellationToken>()).Returns(source);

        // Run 1: only the old Greenhouse binding is live.
        _companies.LiveBindingsAsync(companyId, Arg.Any<CancellationToken>()).Returns([oldBinding]);
        await Handle();
        var addsAfterFirst = _companies.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(ICompanyRepository.AddBindingAsync));

        // Run 2: the migration has landed — the live binding is now Lever/acme-lever, matching detection.
        var newLive = new AtsBinding(
            Guid.CreateVersion7(), companyId, AtsKind.Lever, "acme-lever",
            BindingConfidence.TryCreate(0.95m).Value, "{}", Now);
        _companies.LiveBindingsAsync(companyId, Arg.Any<CancellationToken>()).Returns([newLive]);
        await Handle();
        var addsAfterSecond = _companies.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(ICompanyRepository.AddBindingAsync));

        addsAfterFirst.ShouldBe(1);
        addsAfterSecond.ShouldBe(1); // no second migration
    }
}
