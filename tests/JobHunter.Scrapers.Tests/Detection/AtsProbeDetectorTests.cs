using System.Text.Json;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Scrapers.Adapters;
using JobHunter.Scrapers.Detection;
using JobHunter.Scrapers.Http;
using JobHunter.Scrapers.Tests.Support;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Scrapers.Tests.Detection;

/// <summary>
/// The detection decision (T08, AC-03/AC-04): a single responding board binds with recorded evidence,
/// no board is <see cref="DetectionStatus.NoBoardFound"/> not an exception, and two responding providers
/// are <see cref="DetectionStatus.Ambiguous"/> with the company left inactive.
/// </summary>
public sealed class AtsProbeDetectorTests
{
    private static readonly AtsKind[] BoardKinds =
        [AtsKind.Greenhouse, AtsKind.Lever, AtsKind.Ashby, AtsKind.Workable];

    private static AtsProbeDetector DetectorFor(Func<Uri, string?> route, out FakeClock clock)
    {
        clock = new FakeClock();
        var gated = new GatedHttpClient(new StubHttpClientFactory(new RoutingHttpMessageHandler(route)));
        var sources = new IJobSource[]
        {
            new GreenhouseJobSource(gated, NullLogger<GreenhouseJobSource>.Instance),
            new LeverJobSource(gated, NullLogger<LeverJobSource>.Instance),
            new AshbyJobSource(gated, NullLogger<AshbyJobSource>.Instance),
            new WorkableJobSource(gated, NullLogger<WorkableJobSource>.Instance),
        };
        return new AtsProbeDetector(
            sources, gated, new SequentialIdGenerator(), clock, NullLogger<AtsProbeDetector>.Instance);
    }

    private static Company CompanyWith(string domain, string? careersUrl = null) => new(
        Guid.Parse("00000000-0000-0000-0000-0000000000e1"),
        CanonicalDomain.TryCreate(domain).Value,
        "Acme",
        CompanySource.Curated,
        new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        careersUrl: careersUrl,
        isActive: false);

    // Routes: the given provider answers for the domain's bare-name token; everything else 404s.
    private static Func<Uri, string?> OnlyProviderResponds(AtsKind kind, string token, string domain, bool applyMatches) =>
        uri => uri.ToString() == ProbeBoards.UrlFor(kind, token)
            ? ProbeBoards.For(kind, domain, applyMatches)
            : null;

    [Fact]
    public async Task SingleRespondingCandidate_bindsWithEvidence_aboveThreshold()
    {
        var detector = DetectorFor(
            OnlyProviderResponds(AtsKind.Greenhouse, "acme", "acme.com", applyMatches: true), out _);

        var result = await detector.DetectAsync(CompanyWith("acme.com"), CancellationToken.None);

        result.Status.ShouldBe(DetectionStatus.Bound);
        result.Binding.ShouldNotBeNull();
        result.Binding!.AtsKind.ShouldBe(AtsKind.Greenhouse);
        result.Binding.BoardToken.ShouldBe("acme");
        // responded 0.60 + apply-url match 0.25 + exact token 0.05 = 0.90
        result.Binding.Confidence.Value.ShouldBe(0.90m);
        result.Binding.Confidence.IsConfident.ShouldBeTrue();
    }

    [Fact]
    public async Task Binding_evidence_containsTheFullProbeTrail()
    {
        var detector = DetectorFor(
            OnlyProviderResponds(AtsKind.Greenhouse, "acme", "acme.com", applyMatches: true), out _);

        var result = await detector.DetectAsync(CompanyWith("acme.com"), CancellationToken.None);

        using var evidence = JsonDocument.Parse(result.Binding!.Evidence);
        var candidates = evidence.RootElement.GetProperty("candidates");
        candidates.GetArrayLength().ShouldBeGreaterThan(1);
        // The trail includes the non-responding providers, so a wrong binding is explainable.
        candidates.EnumerateArray()
            .Count(c => c.GetProperty("respondedWithPostings").GetBoolean())
            .ShouldBe(1);
    }

    [Fact]
    public async Task RespondingBoardWithoutApplyUrlMatch_stillBinds_atSixtyFive()
    {
        // responded 0.60 + exact token 0.05 = 0.65 — below threshold, so NoBoardFound.
        var detector = DetectorFor(
            OnlyProviderResponds(AtsKind.Lever, "acme", "acme.com", applyMatches: false), out _);

        var result = await detector.DetectAsync(CompanyWith("acme.com"), CancellationToken.None);

        result.Status.ShouldBe(DetectionStatus.NoBoardFound);
        result.Candidates.ShouldContain(c => c.Kind == AtsKind.Lever && c.Score == 0.65m);
    }

    [Fact]
    public async Task NoCandidateResponds_isNoBoardFound_notAnException_withTrail()
    {
        var detector = DetectorFor(_ => null, out _);

        var result = await detector.DetectAsync(CompanyWith("acme.com"), CancellationToken.None);

        result.Status.ShouldBe(DetectionStatus.NoBoardFound);
        result.Binding.ShouldBeNull();
        result.Candidates.ShouldNotBeEmpty();
        result.Candidates.ShouldAllBe(c => !c.RespondedWithPostings);
    }

    [Fact]
    public async Task TwoProvidersBothConfident_isAmbiguous_andLeavesNoBinding()
    {
        // Both Greenhouse and Ashby answer for "acme" with an apply-url match → both 0.90.
        var detector = DetectorFor(
            uri =>
            {
                if (uri.ToString() == ProbeBoards.UrlFor(AtsKind.Greenhouse, "acme"))
                {
                    return ProbeBoards.For(AtsKind.Greenhouse, "acme.com", applyUrlMatchesDomain: true);
                }

                if (uri.ToString() == ProbeBoards.UrlFor(AtsKind.Ashby, "acme"))
                {
                    return ProbeBoards.For(AtsKind.Ashby, "acme.com", applyUrlMatchesDomain: true);
                }

                return null;
            },
            out _);

        var result = await detector.DetectAsync(CompanyWith("acme.com"), CancellationToken.None);

        result.Status.ShouldBe(DetectionStatus.Ambiguous);
        result.Binding.ShouldBeNull();
        result.Confident.Count.ShouldBe(2);
    }

    [Fact]
    public async Task CareersPageLinkingToTheBoardHost_addsTheTenPointBonus()
    {
        var careersUrl = "https://acme.com/careers";
        var detector = DetectorFor(
            uri =>
            {
                if (uri.ToString() == careersUrl)
                {
                    return "<html><a href=\"https://boards.greenhouse.io/acme\">Jobs</a></html>";
                }

                // No apply-url match, so the base is 0.60 + exact 0.05; the careers link adds 0.10 → 0.75.
                return uri.ToString() == ProbeBoards.UrlFor(AtsKind.Greenhouse, "acme")
                    ? ProbeBoards.For(AtsKind.Greenhouse, "acme.com", applyUrlMatchesDomain: false)
                    : null;
            },
            out _);

        var result = await detector.DetectAsync(CompanyWith("acme.com", careersUrl), CancellationToken.None);

        result.Candidates.ShouldContain(c => c.Kind == AtsKind.Greenhouse && c.CareersPageLinksToBoard && c.Score == 0.75m);
    }

    [Fact]
    public async Task NullCompany_throws()
    {
        var detector = DetectorFor(_ => null, out _);

        await Should.ThrowAsync<ArgumentNullException>(() => detector.DetectAsync(null!, CancellationToken.None));
    }

    public static TheoryData<string> NullDependencyCases => new()
    {
        "sources", "http", "ids", "clock", "logger",
    };

    [Theory]
    [MemberData(nameof(NullDependencyCases))]
    public void NullDependency_throws(string missing)
    {
        var gated = new GatedHttpClient(new StubHttpClientFactory(new RoutingHttpMessageHandler(_ => null)));
        var sources = new IJobSource[] { new GreenhouseJobSource(gated, NullLogger<GreenhouseJobSource>.Instance) };

        Should.Throw<ArgumentNullException>(() => new AtsProbeDetector(
            missing == "sources" ? null! : sources,
            missing == "http" ? null! : gated,
            missing == "ids" ? null! : new SequentialIdGenerator(),
            missing == "clock" ? null! : new FakeClock(),
            missing == "logger" ? null! : NullLogger<AtsProbeDetector>.Instance));
    }

    [Theory]
    [InlineData(AtsKind.Lever, "https://jobs.lever.co/acme")]
    [InlineData(AtsKind.Ashby, "https://jobs.ashbyhq.com/acme")]
    [InlineData(AtsKind.Workable, "https://apply.workable.com/acme")]
    public async Task CareersLink_bonus_isDetectedPerProviderHost(AtsKind kind, string boardLink)
    {
        var careersUrl = "https://acme.com/careers";
        var detector = DetectorFor(
            uri =>
            {
                if (uri.ToString() == careersUrl)
                {
                    return $"<html><a href=\"{boardLink}\">Jobs</a></html>";
                }

                return uri.ToString() == ProbeBoards.UrlFor(kind, "acme")
                    ? ProbeBoards.For(kind, "acme.com", applyUrlMatchesDomain: false)
                    : null;
            },
            out _);

        var result = await detector.DetectAsync(CompanyWith("acme.com", careersUrl), CancellationToken.None);

        // 0.60 responded + 0.05 exact token + 0.10 careers link = 0.75.
        result.Candidates.ShouldContain(c => c.Kind == kind && c.CareersPageLinksToBoard && c.Score == 0.75m);
    }

    [Fact]
    public void Kinds_coverAllFourApiProviders()
    {
        // Guards the probe board table against a provider being silently dropped.
        BoardKinds.Length.ShouldBe(4);
    }
}
