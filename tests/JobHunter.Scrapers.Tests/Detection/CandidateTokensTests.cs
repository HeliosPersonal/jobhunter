using JobHunter.Domain.Companies;
using JobHunter.Scrapers.Detection;
using Shouldly;
using Xunit;

namespace JobHunter.Scrapers.Tests.Detection;

public sealed class CandidateTokensTests
{
    private static CanonicalDomain Domain(string raw) => CanonicalDomain.TryCreate(raw).Value;

    [Fact]
    public void NullDomain_throws()
    {
        Should.Throw<ArgumentNullException>(() => CandidateTokens.Derive(null!, null));
    }

    [Fact]
    public void SimpleDomain_yieldsTheBareLabel_asExact()
    {
        var tokens = CandidateTokens.Derive(Domain("stripe.com"), null);

        tokens.ShouldContain(t => t.Token == "stripe" && t.DerivedFromDomainExactly);
    }

    [Fact]
    public void HyphenatedDomain_yieldsHyphenatedAndConcatenatedForms()
    {
        var tokens = CandidateTokens.Derive(Domain("acme-corp.com"), null);

        tokens.Select(t => t.Token).ShouldContain("acme-corp");
        tokens.Select(t => t.Token).ShouldContain("acmecorp");
    }

    [Fact]
    public void CareersUrl_contributesItsFirstPathSegment_asNonExact()
    {
        var tokens = CandidateTokens.Derive(Domain("acme.com"), "https://boards.greenhouse.io/acmeinc/jobs");

        var careers = tokens.SingleOrDefault(t => t.Token == "acmeinc");
        careers.ShouldNotBeNull();
        careers!.DerivedFromDomainExactly.ShouldBeFalse();
    }

    [Fact]
    public void CareersUrlWithoutPath_addsNoToken()
    {
        var tokens = CandidateTokens.Derive(Domain("acme.com"), "https://acme.com");

        string[] expected = ["acme"];
        tokens.Select(t => t.Token).ShouldBe(expected);
    }

    [Fact]
    public void MalformedCareersUrl_isIgnored()
    {
        var tokens = CandidateTokens.Derive(Domain("acme.com"), "not a url");

        string[] expected = ["acme"];
        tokens.Select(t => t.Token).ShouldBe(expected);
    }

    [Fact]
    public void DuplicateForms_areDeduplicated()
    {
        // A label with no hyphens produces the same string twice; it must appear once.
        var tokens = CandidateTokens.Derive(Domain("acme.com"), null);

        tokens.Count(t => t.Token == "acme").ShouldBe(1);
    }
}
