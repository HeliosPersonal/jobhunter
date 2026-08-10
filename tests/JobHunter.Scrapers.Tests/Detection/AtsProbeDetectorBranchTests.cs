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
/// The detector arms the public-<c>DetectAsync</c> suite (<see cref="AtsProbeDetectorTests"/>) leaves untouched:
/// the <see cref="IBindingDetector"/> port projection that maps each <see cref="DetectionStatus"/> onto its
/// Domain-level <see cref="BindingDetectionStatus"/> (Bound, Ambiguous and the NoBoardFound fallback), and the
/// probe's sample cap — a board answering with more postings than <c>ProbeSampleSize</c> is sampled to the cap and
/// no further (the <c>break</c> arm). All zero-network, driven by the routing handler.
/// </summary>
public sealed class AtsProbeDetectorBranchTests
{
    private static AtsProbeDetector DetectorFor(Func<Uri, string?> route)
    {
        var gated = new GatedHttpClient(new StubHttpClientFactory(new RoutingHttpMessageHandler(route)));
        var sources = new IJobSource[]
        {
            new GreenhouseJobSource(gated, NullLogger<GreenhouseJobSource>.Instance),
            new LeverJobSource(gated, NullLogger<LeverJobSource>.Instance),
            new AshbyJobSource(gated, NullLogger<AshbyJobSource>.Instance),
            new WorkableJobSource(gated, NullLogger<WorkableJobSource>.Instance),
        };
        return new AtsProbeDetector(
            sources, gated, new SequentialIdGenerator(), new FakeClock(), NullLogger<AtsProbeDetector>.Instance);
    }

    private static Company CompanyWith(string domain) => new(
        Guid.Parse("00000000-0000-0000-0000-0000000000e1"),
        CanonicalDomain.TryCreate(domain).Value,
        "Acme",
        CompanySource.Curated,
        new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        careersUrl: null,
        isActive: false);

    private static Func<Uri, string?> OnlyGreenhouseResponds =>
        uri => uri.ToString() == ProbeBoards.UrlFor(AtsKind.Greenhouse, "acme")
            ? ProbeBoards.For(AtsKind.Greenhouse, "acme.com", applyUrlMatchesDomain: true)
            : null;

    [Fact]
    public async Task The_port_projects_a_single_confident_board_as_Bound()
    {
        IBindingDetector detector = DetectorFor(OnlyGreenhouseResponds);

        var result = await detector.DetectAsync(CompanyWith("acme.com"), CancellationToken.None);

        result.Status.ShouldBe(BindingDetectionStatus.Bound);
        result.Binding.ShouldNotBeNull();
        result.Binding!.AtsKind.ShouldBe(AtsKind.Greenhouse);
    }

    [Fact]
    public async Task The_port_projects_no_responding_board_as_NoBoardFound()
    {
        IBindingDetector detector = DetectorFor(_ => null);

        var result = await detector.DetectAsync(CompanyWith("acme.com"), CancellationToken.None);

        result.Status.ShouldBe(BindingDetectionStatus.NoBoardFound);
        result.Binding.ShouldBeNull();
    }

    [Fact]
    public async Task The_port_projects_two_confident_providers_as_Ambiguous()
    {
        IBindingDetector detector = DetectorFor(uri =>
        {
            if (uri.ToString() == ProbeBoards.UrlFor(AtsKind.Greenhouse, "acme"))
            {
                return ProbeBoards.For(AtsKind.Greenhouse, "acme.com", applyUrlMatchesDomain: true);
            }

            return uri.ToString() == ProbeBoards.UrlFor(AtsKind.Ashby, "acme")
                ? ProbeBoards.For(AtsKind.Ashby, "acme.com", applyUrlMatchesDomain: true)
                : null;
        });

        var result = await detector.DetectAsync(CompanyWith("acme.com"), CancellationToken.None);

        result.Status.ShouldBe(BindingDetectionStatus.Ambiguous);
        result.Binding.ShouldBeNull();
    }

    [Fact]
    public async Task A_board_with_more_than_the_sample_cap_is_sampled_to_the_cap_and_no_further()
    {
        // Six jobs on the board; the probe reads five and breaks, so PostingsSeen is capped at ProbeSampleSize.
        var jobs = string.Join(",", Enumerable.Range(0, 6).Select(i =>
            $$"""{"id":{{1000 + i}},"title":"Engineer {{i}}","absolute_url":"https://acme.com/careers/{{i}}","content":"<p>Role</p>","updated_at":"2026-07-01T00:00:00Z"}"""));
        var board = $$"""{"jobs":[{{jobs}}]}""";

        var detector = DetectorFor(uri =>
            uri.ToString() == ProbeBoards.UrlFor(AtsKind.Greenhouse, "acme") ? board : null);

        var result = await detector.DetectAsync(CompanyWith("acme.com"), CancellationToken.None);

        var greenhouse = result.Candidates.Single(c => c.Kind == AtsKind.Greenhouse && c.RespondedWithPostings);
        greenhouse.PostingsSeen.ShouldBe(5);
    }
}
