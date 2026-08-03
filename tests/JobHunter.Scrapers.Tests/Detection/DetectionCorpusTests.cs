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
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace JobHunter.Scrapers.Tests.Detection;

/// <summary>
/// The labelled detection corpus (T08, AC-03/AC-04). Fifty companies with a known outcome are each replayed
/// as a zero-network probe; accuracy must stay ≥ 47/50. The corpus is the credibility test for the scoring
/// table — it asserts the detector binds the right board or binds nothing, and never attributes another
/// company's jobs.
/// </summary>
public sealed class DetectionCorpusTests
{
    private const int MinimumCorrect = 47;

    private static List<CorpusCase> LoadCorpus()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "detection-set.yaml");
        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        return deserializer.Deserialize<Corpus>(yaml).Companies;
    }

    [Fact]
    public void Corpus_hasFiftyLabelledCompanies()
    {
        LoadCorpus().Count.ShouldBe(50);
    }

    [Fact]
    public async Task Detector_isAtLeastNinetyFivePercentAccurate_onTheLabelledSet()
    {
        var corpus = LoadCorpus();
        var correct = 0;
        var misses = new List<string>();

        foreach (var company in corpus)
        {
            var result = await DetectOne(company);
            if (Matches(company.Expect, result))
            {
                correct++;
            }
            else
            {
                misses.Add($"{company.Domain}: expected {company.Expect}, got {Describe(result)}");
            }
        }

        correct.ShouldBeGreaterThanOrEqualTo(
            MinimumCorrect,
            $"detection accuracy fell below {MinimumCorrect}/50. Misses:{Environment.NewLine}{string.Join(Environment.NewLine, misses)}");
    }

    [Fact]
    public async Task Detector_neverMisattributes_onTheLabelledSet()
    {
        // The one failure that must never happen: binding a company to a board that is not its own.
        var corpus = LoadCorpus();
        foreach (var company in corpus)
        {
            var result = await DetectOne(company);
            if (result.Status != DetectionStatus.Bound)
            {
                continue;
            }

            var boundKind = result.Binding!.AtsKind;
            company.Boards.ShouldContain(
                b => KindOf(b.Provider) == boundKind,
                $"{company.Domain} was bound to {boundKind}, which is not one of its real boards.");
        }
    }

    private static async Task<DetectionResult> DetectOne(CorpusCase company)
    {
        var domain = CanonicalDomain.TryCreate(company.Domain).Value;
        var routes = company.Boards.ToDictionary(
            b => ProbeBoards.UrlFor(KindOf(b.Provider), b.Token),
            b => ProbeBoards.For(KindOf(b.Provider), domain.Value, b.ApplyMatches),
            StringComparer.Ordinal);

        var gated = new GatedHttpClient(new StubHttpClientFactory(
            new RoutingHttpMessageHandler(uri => routes.GetValueOrDefault(uri.ToString()))));

        var sources = new IJobSource[]
        {
            new GreenhouseJobSource(gated, NullLogger<GreenhouseJobSource>.Instance),
            new LeverJobSource(gated, NullLogger<LeverJobSource>.Instance),
            new AshbyJobSource(gated, NullLogger<AshbyJobSource>.Instance),
            new WorkableJobSource(gated, NullLogger<WorkableJobSource>.Instance),
        };
        var detector = new AtsProbeDetector(
            sources, gated, new SequentialIdGenerator(), new FakeClock(), NullLogger<AtsProbeDetector>.Instance);

        var subject = new Company(
            Guid.Parse("00000000-0000-0000-0000-0000000000c1"),
            domain,
            company.Domain,
            CompanySource.Curated,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            careersUrl: company.CareersUrl,
            isActive: false);

        return await detector.DetectAsync(subject, CancellationToken.None);
    }

    private static bool Matches(string expect, DetectionResult result) => expect switch
    {
        "none" => result.Status == DetectionStatus.NoBoardFound,
        "ambiguous" => result.Status == DetectionStatus.Ambiguous,
        _ => result.Status == DetectionStatus.Bound && result.Binding!.AtsKind == KindOf(expect),
    };

    private static string Describe(DetectionResult result) => result.Status switch
    {
        DetectionStatus.Bound => $"bound:{result.Binding!.AtsKind}",
        _ => result.Status.ToString(),
    };

    private static AtsKind KindOf(string provider) => provider switch
    {
        "greenhouse" => AtsKind.Greenhouse,
        "lever" => AtsKind.Lever,
        "ashby" => AtsKind.Ashby,
        "workable" => AtsKind.Workable,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider in corpus."),
    };

    private sealed class Corpus
    {
        public List<CorpusCase> Companies { get; init; } = [];
    }

    private sealed class CorpusCase
    {
        public string Domain { get; init; } = string.Empty;

        public string? CareersUrl { get; init; }

        public List<CorpusBoard> Boards { get; init; } = [];

        public string Expect { get; init; } = string.Empty;
    }

    private sealed class CorpusBoard
    {
        public string Provider { get; init; } = string.Empty;

        public string Token { get; init; } = string.Empty;

        public bool ApplyMatches { get; init; }
    }
}
