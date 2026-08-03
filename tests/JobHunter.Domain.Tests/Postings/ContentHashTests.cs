using JobHunter.Domain.Postings;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Postings;

public sealed class ContentHashTests
{
    [Fact]
    public void Compute_is_deterministic_and_64_lowercase_hex()
    {
        var a = ContentHash.Compute("{\"title\":\"SRE\"}");
        var b = ContentHash.Compute("{\"title\":\"SRE\"}");

        a.ShouldBe(b);
        a.Value.Length.ShouldBe(64);
        a.Value.ShouldBe(a.Value.ToLowerInvariant());
    }

    [Fact]
    public void Different_content_produces_a_different_hash()
    {
        ContentHash.Compute("a").ShouldNotBe(ContentHash.Compute("b"));
    }

    [Fact]
    public void Compute_matches_the_known_sha256_of_the_empty_string()
    {
        ContentHash.Compute(string.Empty).Value
            .ShouldBe("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public void TryCreate_accepts_a_well_formed_digest()
    {
        var digest = ContentHash.Compute("payload").Value;

        var result = ContentHash.TryCreate(digest);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(digest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tooshort")]
    [InlineData("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")] // uppercase
    [InlineData("g3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")] // non-hex 'g'
    public void TryCreate_rejects_malformed_input(string? value)
    {
        var result = ContentHash.TryCreate(value);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ContentHash.Invalid);
    }

    [Fact]
    public void ToString_returns_the_hex_value()
    {
        var hash = ContentHash.Compute("x");
        hash.ToString().ShouldBe(hash.Value);
    }
}
