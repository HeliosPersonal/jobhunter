using JobHunter.Domain.Companies;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Companies;

public sealed class CanonicalDomainTests
{
    [Theory]
    [InlineData("stripe.com", "stripe.com")]
    [InlineData("www.stripe.com", "stripe.com")]
    [InlineData("https://stripe.com/careers", "stripe.com")]
    [InlineData("https://www.Stripe.com/careers?ref=1", "stripe.com")]
    [InlineData("http://user:pass@careers.stripe.com:8443/jobs", "stripe.com")]
    [InlineData("STRIPE.COM.", "stripe.com")]
    [InlineData("careers.stripe.com", "stripe.com")]
    public void Canonicalises_equivalent_urls_to_the_same_registrable_domain(string raw, string expected)
    {
        var result = CanonicalDomain.TryCreate(raw);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("foo.github.io", "foo.github.io")]
    [InlineData("bar.github.io", "bar.github.io")]
    [InlineData("team.herokuapp.com", "team.herokuapp.com")]
    public void Private_suffixes_keep_subdomains_distinct(string raw, string expected)
    {
        CanonicalDomain.TryCreate(raw).Value.Value.ShouldBe(expected);
    }

    [Fact]
    public void Two_github_io_tenants_are_not_equal()
    {
        var a = CanonicalDomain.TryCreate("foo.github.io").Value;
        var b = CanonicalDomain.TryCreate("bar.github.io").Value;

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Uk_second_level_suffix_is_respected()
    {
        CanonicalDomain.TryCreate("shop.co.uk").Value.Value.ShouldBe("shop.co.uk");
        CanonicalDomain.TryCreate("www.shop.co.uk").Value.Value.ShouldBe("shop.co.uk");
    }

    [Fact]
    public void Idn_host_is_converted_to_punycode()
    {
        var result = CanonicalDomain.TryCreate("bücher.de");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe("xn--bcher-kva.de");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("localhost")]
    [InlineData("com")]
    [InlineData("co.uk")]
    [InlineData("192.168.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("https://10.0.0.5/jobs")]
    public void Rejects_non_domains(string? raw)
    {
        var result = CanonicalDomain.TryCreate(raw);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(CanonicalDomain.Invalid);
    }

    [Fact]
    public void Malformed_idn_label_is_rejected_not_thrown()
    {
        // A label starting with a combining character is not a valid IDN.
        var result = CanonicalDomain.TryCreate("́bad.com");

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Equal_domains_share_a_hash_code()
    {
        var a = CanonicalDomain.TryCreate("https://www.stripe.com").Value;
        var b = CanonicalDomain.TryCreate("stripe.com").Value;

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
        a.ToString().ShouldBe("stripe.com");
    }
}
