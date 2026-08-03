using JobHunter.Domain.Companies;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Companies;

public sealed class PublicSuffixListTests
{
    [Theory]
    [InlineData("stripe.com", "stripe.com")]
    [InlineData("careers.stripe.com", "stripe.com")]
    [InlineData("a.b.c.stripe.com", "stripe.com")]
    [InlineData("shop.co.uk", "shop.co.uk")]
    [InlineData("deep.shop.co.uk", "shop.co.uk")]
    [InlineData("example.co.jp", "example.co.jp")]
    [InlineData("foo.github.io", "foo.github.io")]
    public void Ordinary_and_private_rules_reduce_to_registrable_domain(string host, string expected)
    {
        PublicSuffixList.GetRegistrableDomain(host).ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("com")]
    [InlineData("co.uk")]
    [InlineData("github.io")]
    [InlineData("bad..dotted.com")]
    public void Returns_null_when_there_is_no_registrable_part(string host)
    {
        PublicSuffixList.GetRegistrableDomain(host).ShouldBeNull();
    }

    [Fact]
    public void Unlisted_tld_uses_the_default_single_label_rule()
    {
        // "quux" is not a known suffix; default "*" rule makes it the public suffix.
        PublicSuffixList.GetRegistrableDomain("acme.quux").ShouldBe("acme.quux");
        PublicSuffixList.GetRegistrableDomain("a.acme.quux").ShouldBe("acme.quux");
    }

    [Fact]
    public void Wildcard_rule_treats_any_label_as_part_of_the_suffix()
    {
        // *.kawasaki.jp — foo.kawasaki.jp is a public suffix, so a registrable domain needs one more label.
        PublicSuffixList.GetRegistrableDomain("foo.kawasaki.jp").ShouldBeNull();
        PublicSuffixList.GetRegistrableDomain("shop.foo.kawasaki.jp").ShouldBe("shop.foo.kawasaki.jp");
    }

    [Fact]
    public void Exception_rule_beats_the_wildcard()
    {
        // !city.kawasaki.jp — city.kawasaki.jp is NOT a suffix; kawasaki.jp is, so this reduces here.
        PublicSuffixList.GetRegistrableDomain("city.kawasaki.jp").ShouldBe("city.kawasaki.jp");
    }
}
