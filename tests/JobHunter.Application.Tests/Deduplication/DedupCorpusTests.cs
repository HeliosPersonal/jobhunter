using JobHunter.Application.Deduplication;
using JobHunter.Application.Normalization;
using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace JobHunter.Application.Tests.Deduplication;

/// <summary>
/// F2's centre of gravity: the labelled dedup corpus (test-plan §The dedup corpus). Every pair carries the
/// two postings, an <c>expect</c> of <c>merge</c> or <c>distinct</c>, and the reason. Each side is run
/// through the <em>real</em> deterministic pipeline — <see cref="TitleNormalizer"/>, <see cref="LocationParser"/>
/// and <see cref="FingerprintCalculator"/> — and a pair merges exactly when the two fingerprints are equal.
///
/// <para><strong>Zero false merges, or the build fails (QG-1):</strong> a <c>distinct</c> pair whose
/// fingerprints collide is the one defect this feature exists to prevent, so it is a hard failure listing
/// every offender. False splits — a <c>merge</c> pair the conservative fingerprint keeps apart — are
/// tolerated up to 5% of the merge-labelled pairs (the fingerprint deliberately errs toward splitting), and
/// the display-time grouping (AC-10) catches the rest.</para>
/// </summary>
public sealed class DedupCorpusTests
{
    private const double MaxFalseSplitRatio = 0.05;

    private static readonly List<CorpusPair> Corpus = LoadCorpus();

    [Fact]
    public void The_corpus_has_at_least_two_hundred_labelled_pairs()
    {
        Corpus.Count.ShouldBeGreaterThanOrEqualTo(200);
    }

    [Fact]
    public void Not_one_distinct_pair_false_merges()
    {
        var falseMerges = Corpus
            .Where(p => p.Expect == Label.Distinct)
            .Where(p => Fingerprint(p.A) == Fingerprint(p.B))
            .Select(p => $"#{p.Id}: {p.Why}")
            .ToList();

        falseMerges.ShouldBeEmpty(
            "A false merge is the one defect F2 must never ship (QG-1). Offending distinct pairs: "
            + string.Join("; ", falseMerges));
    }

    [Fact]
    public void False_splits_stay_within_the_tolerated_five_percent()
    {
        var mergePairs = Corpus.Where(p => p.Expect == Label.Merge).ToList();
        var falseSplits = mergePairs.Count(p => Fingerprint(p.A) != Fingerprint(p.B));

        var ratio = (double)falseSplits / mergePairs.Count;
        ratio.ShouldBeLessThanOrEqualTo(
            MaxFalseSplitRatio,
            $"{falseSplits}/{mergePairs.Count} merge-labelled pairs split; the fingerprint is too coarse.");
    }

    [Fact]
    public void Every_merge_labelled_pair_that_splits_is_only_a_near_duplicate_not_a_hard_merge()
    {
        // A merge pair may split only when it is a genuine near-duplicate (different title wording, caught
        // by display grouping). A pair whose two sides are identical after normalisation must never split.
        var identicalButSplit = Corpus
            .Where(p => p.Expect == Label.Merge)
            .Where(p => Normalise(p.A) == Normalise(p.B))
            .Where(p => Fingerprint(p.A) != Fingerprint(p.B))
            .Select(p => $"#{p.Id}")
            .ToList();

        identicalButSplit.ShouldBeEmpty(
            "These pairs normalise identically yet split — the fingerprint is non-deterministic: "
            + string.Join(", ", identicalButSplit));
    }

    private static string Fingerprint(Posting posting)
    {
        var normalisedTitle = TitleNormalizer.Normalize(posting.Title).Value;
        var locations = LocationSetFor(posting);
        return FingerprintCalculator.Compute(posting.Domain, normalisedTitle, locations).Value;
    }

    private static (string, string) Normalise(Posting posting) =>
        (TitleNormalizer.Normalize(posting.Title).Value, LocationSetFor(posting).SortedKey);

    private static LocationSet LocationSetFor(Posting posting)
    {
        if (posting.Locations.Count == 0)
        {
            return LocationSet.Empty;
        }

        var built = new List<JobLocation>();
        foreach (var location in posting.Locations)
        {
            var created = JobLocation.TryCreate(location.Country, location.Region, location.City);
            if (created.IsSuccess)
            {
                built.Add(created.Value);
            }
        }

        return LocationSet.Of(built);
    }

    private static List<CorpusPair> LoadCorpus()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "dedup-corpus.yaml");
        var stream = new YamlStream();
        using (var reader = new StreamReader(path))
        {
            stream.Load(reader);
        }

        var root = (YamlSequenceNode)stream.Documents[0].RootNode;
        var pairs = new List<CorpusPair>(root.Children.Count);
        foreach (var node in root.Children)
        {
            var mapping = (YamlMappingNode)node;
            pairs.Add(new CorpusPair(
                int.Parse(Scalar(mapping, "id"), System.Globalization.CultureInfo.InvariantCulture),
                ParseLabel(Scalar(mapping, "expect")),
                Scalar(mapping, "why"),
                ParsePosting((YamlMappingNode)mapping.Children[new YamlScalarNode("a")]),
                ParsePosting((YamlMappingNode)mapping.Children[new YamlScalarNode("b")])));
        }

        return pairs;
    }

    private static Posting ParsePosting(YamlMappingNode mapping)
    {
        var locations = new List<Location>();
        if (mapping.Children.TryGetValue(new YamlScalarNode("locations"), out var node)
            && node is YamlSequenceNode sequence)
        {
            foreach (var child in sequence.Children.Cast<YamlMappingNode>())
            {
                locations.Add(new Location(
                    OptionalScalar(child, "country"),
                    OptionalScalar(child, "region"),
                    OptionalScalar(child, "city")));
            }
        }

        return new Posting(Scalar(mapping, "domain"), Scalar(mapping, "title"), locations);
    }

    private static Label ParseLabel(string value) => value switch
    {
        "merge" => Label.Merge,
        "distinct" => Label.Distinct,
        _ => throw new InvalidOperationException($"Unknown corpus label '{value}'."),
    };

    private static string Scalar(YamlMappingNode mapping, string key) =>
        ((YamlScalarNode)mapping.Children[new YamlScalarNode(key)]).Value!;

    private static string? OptionalScalar(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar
            ? scalar.Value
            : null;

    private enum Label
    {
        Merge,
        Distinct,
    }

    private sealed record CorpusPair(int Id, Label Expect, string Why, Posting A, Posting B);

    private sealed record Posting(string Domain, string Title, IReadOnlyList<Location> Locations);

    private sealed record Location(string? Country, string? Region, string? City);
}
