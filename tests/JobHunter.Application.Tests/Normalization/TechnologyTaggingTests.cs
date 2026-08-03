using JobHunter.Application.Deduplication;
using JobHunter.Application.Normalization;
using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Normalization;

/// <summary>
/// T07: the deterministic technology tagger. Every accepted spelling maps to one canonical name; matching
/// is whole-token only, so a short language name never hits inside a longer word; and a hit records whether
/// it came from the title or the description so a title match can be weighted later. Tagging is a pure
/// function of the vocabulary and the job's two texts — no clock, no model, no I/O — and it never touches
/// F3's inferred store, keeping the deterministic set separable (data-model §job_technologies).
/// </summary>
public sealed class TechnologyTaggingTests
{
    private static readonly DateTimeOffset Seen = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    private static readonly TechnologyEntry[] Entries =
    [
        new("Go", ["golang"]),
        new("C#", ["csharp"]),
        new("C++", ["cpp"]),
        new(".NET", ["dotnet"]),
        new("ASP.NET", ["aspnet"]),
        new("Node.js", ["nodejs", "node"]),
        new("Kubernetes", ["k8s"]),
        new("React", ["reactjs"]),
    ];

    private static TechnologyVocabulary Vocabulary() => new(Entries);

    [Theory]
    [InlineData("golang")]
    [InlineData("Golang")]
    [InlineData("GOLANG")]
    [InlineData("Go")]
    public void All_spellings_of_a_technology_map_to_the_one_canonical_name(string spelling)
    {
        Vocabulary().Match($"We use {spelling} in production.").ShouldContain("Go");
    }

    [Theory]
    [InlineData("csharp", "C#")]
    [InlineData("cpp", "C++")]
    [InlineData("dotnet", ".NET")]
    [InlineData("k8s", "Kubernetes")]
    [InlineData("reactjs", "React")]
    public void An_alias_resolves_to_its_canonical_name(string alias, string canonical)
    {
        Vocabulary().Match($"Experience with {alias} required.").ShouldContain(canonical);
    }

    [Theory]
    [InlineData("We love Google internally.")]      // "Go" must not match inside "Google"
    [InlineData("Django is our web framework.")]    // "Go" must not match inside "Django"
    [InlineData("A good attitude is a plus.")]      // "Go" must not match inside "good"
    public void A_short_name_does_not_match_inside_a_longer_word(string text)
    {
        Vocabulary().Match(text).ShouldNotContain("Go");
    }

    [Fact]
    public void A_dotted_name_does_not_match_inside_a_longer_dotted_name()
    {
        // ".NET" appears inside "ASP.NET" textually but must not tag .NET — the '.' is a token character,
        // so the whole token is "asp.net", which tags ASP.NET only.
        var matches = Vocabulary().Match("We build on ASP.NET here.");

        matches.ShouldContain("ASP.NET");
        matches.ShouldNotContain(".NET");
    }

    [Fact]
    public void Symbol_names_match_as_whole_tokens()
    {
        var matches = Vocabulary().Match("Strong C# and C++ background, plus Node.js.");

        matches.ShouldContain("C#");
        matches.ShouldContain("C++");
        matches.ShouldContain("Node.js");
    }

    [Fact]
    public void Separators_other_than_letters_split_tokens()
    {
        // A hyphen and a slash are not token characters, so a flanked term still matches.
        Vocabulary().Match("stack: Go/Kubernetes-first").ShouldContain("Go");
        Vocabulary().Match("stack: Go/Kubernetes-first").ShouldContain("Kubernetes");
    }

    [Fact]
    public void Each_canonical_name_is_returned_at_most_once_even_with_several_hits()
    {
        var matches = Vocabulary().Match("Go and golang and more Go.");

        matches.Count(m => m == "Go").ShouldBe(1);
    }

    [Fact]
    public void Empty_or_blank_text_matches_nothing()
    {
        Vocabulary().Match(null).ShouldBeEmpty();
        Vocabulary().Match("   ").ShouldBeEmpty();
    }

    [Fact]
    public void A_blank_canonical_name_is_rejected_at_construction()
    {
        TechnologyEntry[] entries = [new("  ")];
        Should.Throw<ArgumentException>(() => new TechnologyVocabulary(entries));
    }

    [Fact]
    public void A_duplicated_canonical_name_is_rejected_at_construction()
    {
        TechnologyEntry[] entries = [new("Go"), new("Go")];
        Should.Throw<ArgumentException>(() => new TechnologyVocabulary(entries));
    }

    [Fact]
    public void A_term_claimed_by_two_canonical_names_is_rejected_at_construction()
    {
        TechnologyEntry[] entries = [new("Go", ["shared"]), new("Rust", ["shared"])];
        Should.Throw<ArgumentException>(() => new TechnologyVocabulary(entries));
    }

    [Fact]
    public void A_title_hit_is_recorded_as_title_and_a_body_only_hit_as_description()
    {
        var job = JobWith(title: "Senior Go Engineer", description: "You will use Kubernetes daily.");

        new TechnologyTagger(Vocabulary()).Tag(job);

        job.Technologies.ShouldContain(t => t.Technology == "Go" && t.MatchedVia == TechnologyMatch.Title);
        job.Technologies.ShouldContain(t => t.Technology == "Kubernetes" && t.MatchedVia == TechnologyMatch.Description);
    }

    [Fact]
    public void A_technology_in_both_title_and_description_is_recorded_once_via_title()
    {
        var job = JobWith(title: "Go Engineer", description: "Deep Go experience required.");

        new TechnologyTagger(Vocabulary()).Tag(job);

        var go = job.Technologies.ShouldHaveSingleItem();
        go.Technology.ShouldBe("Go");
        go.MatchedVia.ShouldBe(TechnologyMatch.Title);
    }

    [Fact]
    public void Tagging_is_idempotent_over_repeated_runs()
    {
        var job = JobWith(title: "Go Engineer", description: "Kubernetes.");
        var tagger = new TechnologyTagger(Vocabulary());

        tagger.Tag(job);
        tagger.Tag(job);

        job.Technologies.Count.ShouldBe(2);
    }

    private static Job JobWith(string title, string description)
    {
        var normalised = TitleNormalizer.Normalize(title);
        var fingerprint = FingerprintCalculator.Compute("acme.com", normalised.Value, LocationSet.Empty);

        return new Job(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            fingerprint,
            FingerprintCalculator.Version,
            title,
            normalised.Value,
            description,
            "https://acme.com/apply/1",
            LocationSet.Empty,
            RemotePolicy.Unknown,
            EmploymentType.Unknown,
            PostedAtGranularity.Exact,
            Seen,
            Seen);
    }
}
