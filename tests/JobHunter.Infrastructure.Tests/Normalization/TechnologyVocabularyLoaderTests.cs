using JobHunter.Infrastructure.Normalization;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Normalization;

/// <summary>
/// T07: the committed technology vocabulary loads from the embedded YAML into a usable, unambiguous
/// vocabulary — the load that runs at host startup, so a malformed or ambiguous file fails here rather than
/// mis-tagging at run time. A handful of anchor technologies prove the file is wired and parsed, without the
/// test policing every one of the ~300 entries (that is the diff's job).
/// </summary>
public sealed class TechnologyVocabularyLoaderTests
{
    [Fact]
    public void The_embedded_vocabulary_loads_and_is_substantial()
    {
        var vocabulary = TechnologyVocabularyLoader.Load();

        vocabulary.Count.ShouldBeGreaterThan(200);
    }

    [Theory]
    [InlineData("We build services in Go and Rust.", "Go")]
    [InlineData("Strong C# and .NET background.", "C#")]
    [InlineData("Deploy to Kubernetes (k8s).", "Kubernetes")]
    [InlineData("Experience with golang required.", "Go")]
    [InlineData("Our stack is Node.js and PostgreSQL.", "PostgreSQL")]
    public void The_loaded_vocabulary_tags_anchor_technologies(string text, string expected)
    {
        TechnologyVocabularyLoader.Load().Match(text).ShouldContain(expected);
    }

    [Fact]
    public void A_non_sequence_document_is_a_named_failure()
    {
        var ex = Should.Throw<TechnologyVocabularyException>(() =>
            TechnologyVocabularyLoader.Parse("canonical: Go"));

        ex.Message.ShouldContain("sequence");
    }

    [Fact]
    public void An_entry_with_no_canonical_name_is_a_named_failure()
    {
        var ex = Should.Throw<TechnologyVocabularyException>(() =>
            TechnologyVocabularyLoader.Parse("- aliases: [foo]"));

        ex.Message.ShouldContain("canonical");
    }

    [Fact]
    public void An_ambiguous_vocabulary_is_a_named_failure()
    {
        // "shared" is claimed by two canonical names — the vocabulary would tag inconsistently, so the load
        // is rejected with the offending spelling named.
        var yaml =
            """
            - canonical: Go
              aliases: [shared]
            - canonical: Rust
              aliases: [shared]
            """;

        var ex = Should.Throw<TechnologyVocabularyException>(() => TechnologyVocabularyLoader.Parse(yaml));

        ex.Message.ShouldContain("shared");
    }

    [Fact]
    public void An_empty_document_yields_an_empty_vocabulary()
    {
        TechnologyVocabularyLoader.Parse(string.Empty).Count.ShouldBe(0);
    }
}
