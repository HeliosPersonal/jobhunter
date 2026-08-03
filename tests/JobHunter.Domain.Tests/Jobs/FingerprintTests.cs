using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Jobs;

public sealed class FingerprintTests
{
    private const string ValidHex = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void A_well_formed_digest_is_accepted()
    {
        var result = Fingerprint.TryCreate(ValidHex);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(ValidHex);
        result.Value.ToString().ShouldBe(ValidHex);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public void A_short_or_null_value_is_rejected(string? value)
    {
        var result = Fingerprint.TryCreate(value);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Fingerprint.Invalid);
    }

    [Fact]
    public void An_uppercase_digest_is_rejected()
    {
        Fingerprint.TryCreate(ValidHex.ToUpperInvariant()).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_non_hex_character_is_rejected()
    {
        var withG = "g" + ValidHex[1..];

        Fingerprint.TryCreate(withG).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Equality_is_by_value()
    {
        var a = Fingerprint.TryCreate(ValidHex).Value;
        var b = Fingerprint.TryCreate(ValidHex).Value;

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }
}
