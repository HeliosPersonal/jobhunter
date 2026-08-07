using JobHunter.Application.Research;
using JobHunter.Application.Tests.Support;
using JobHunter.Domain.Research;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Research;

/// <summary>
/// T07 — the uncited-claim suite (QG-1). The verifier is the one rule the whole feature rests on: a claim
/// is kept only if its cited URL is a member of the set of URLs actually fetched <em>for this dossier</em>,
/// matched by exact set membership after normalising scheme, host case and a trailing slash — and nothing
/// else. A URL "close to" a real one is precisely the failure mode being guarded against, so a different
/// path, an injected query parameter, or a URL borrowed from another company's research is discarded, never
/// stored. Every discarded claim is counted and its fabricated URL is logged (AC-08).
/// </summary>
public sealed class ClaimVerifierTests
{
    private static readonly DateTimeOffset Observed = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

    private static ResearchSource Source(string url, ResearchCategory category = ResearchCategory.EngineeringBlog) =>
        new(Guid.NewGuid(), category, url, "Title", url.Length, Observed);

    private static UnverifiedClaim Claim(string sourceUrl, ResearchCategory category = ResearchCategory.EngineeringBlog) =>
        new(category, $"A claim citing {sourceUrl}.", sourceUrl, IsWarning: false);

    private static ClaimVerifier NewVerifier(out CapturingLogger<ClaimVerifier> logger)
    {
        logger = new CapturingLogger<ClaimVerifier>();
        return new ClaimVerifier(logger);
    }

    [Fact]
    public void All_claims_citing_real_fetched_urls_are_all_stored_with_zero_discarded()
    {
        var sources = new[] { Source("https://acme.ai/blog"), Source("https://acme.ai/eng") };
        var claims = new[] { Claim("https://acme.ai/blog"), Claim("https://acme.ai/eng") };

        var result = NewVerifier(out _).Verify(sources, claims);

        result.Verified.Count.ShouldBe(2);
        result.Discarded.ShouldBe(0);
    }

    [Fact]
    public void One_claim_citing_a_never_fetched_url_is_the_only_one_discarded()
    {
        var sources = new[] { Source("https://acme.ai/blog") };
        var claims = new[]
        {
            Claim("https://acme.ai/blog"),
            Claim("https://acme.ai/press/series-b"),
        };

        var result = NewVerifier(out _).Verify(sources, claims);

        result.Verified.Count.ShouldBe(1);
        result.Verified[0].Source.Url.ShouldBe("https://acme.ai/blog");
        result.Discarded.ShouldBe(1);
    }

    [Fact]
    public void A_trailing_slash_difference_still_matches()
    {
        var sources = new[] { Source("https://acme.ai/blog") };
        var claims = new[] { Claim("https://acme.ai/blog/") };

        var result = NewVerifier(out _).Verify(sources, claims);

        result.Verified.Count.ShouldBe(1);
        result.Discarded.ShouldBe(0);
    }

    [Fact]
    public void A_host_case_difference_still_matches()
    {
        var sources = new[] { Source("https://acme.ai/blog") };
        var claims = new[] { Claim("https://ACME.ai/blog") };

        var result = NewVerifier(out _).Verify(sources, claims);

        result.Verified.Count.ShouldBe(1);
        result.Discarded.ShouldBe(0);
    }

    [Fact]
    public void A_scheme_case_difference_still_matches()
    {
        var sources = new[] { Source("https://acme.ai/blog") };
        var claims = new[] { Claim("HTTPS://acme.ai/blog") };

        var result = NewVerifier(out _).Verify(sources, claims);

        result.Verified.Count.ShouldBe(1);
        result.Discarded.ShouldBe(0);
    }

    [Fact]
    public void A_different_path_on_the_same_host_is_discarded()
    {
        var sources = new[] { Source("https://acme.ai/blog") };
        var claims = new[] { Claim("https://acme.ai/blog/2024/layoffs") };

        var result = NewVerifier(out _).Verify(sources, claims);

        result.Verified.ShouldBeEmpty();
        result.Discarded.ShouldBe(1);
    }

    [Fact]
    public void A_url_from_another_companys_dossier_is_discarded()
    {
        // Only this dossier's fetched sources are passed — a URL genuinely fetched, but for another
        // company, is not in this set and must be discarded.
        var sources = new[] { Source("https://acme.ai/blog") };
        var claims = new[] { Claim("https://globex.com/blog") };

        var result = NewVerifier(out _).Verify(sources, claims);

        result.Verified.ShouldBeEmpty();
        result.Discarded.ShouldBe(1);
    }

    [Fact]
    public void A_real_url_with_an_injected_query_parameter_is_discarded()
    {
        var sources = new[] { Source("https://acme.ai/blog") };
        var claims = new[] { Claim("https://acme.ai/blog?utm_source=x") };

        var result = NewVerifier(out _).Verify(sources, claims);

        result.Verified.ShouldBeEmpty();
        result.Discarded.ShouldBe(1);
    }

    [Fact]
    public void When_every_claim_is_fabricated_nothing_is_verified_and_all_are_counted()
    {
        var sources = new[] { Source("https://acme.ai/blog") };
        var claims = new[]
        {
            Claim("https://acme.ai/invented"),
            Claim("https://not-acme.ai/blog"),
        };

        var result = NewVerifier(out _).Verify(sources, claims);

        result.Verified.ShouldBeEmpty();
        result.Discarded.ShouldBe(2);
    }

    [Fact]
    public void A_verified_claim_resolves_to_the_exact_fetched_source_object()
    {
        var blog = Source("https://acme.ai/blog", ResearchCategory.EngineeringBlog);
        var careers = Source("https://acme.ai/careers", ResearchCategory.InterviewProcess);
        var claims = new[]
        {
            new UnverifiedClaim(ResearchCategory.Layoffs, "They cut staff.", "https://acme.ai/careers", IsWarning: true),
        };

        var result = NewVerifier(out _).Verify([blog, careers], claims);

        var verified = result.Verified.ShouldHaveSingleItem();
        // The claim's own category is authoritative and need not equal the source's category.
        verified.Category.ShouldBe(ResearchCategory.Layoffs);
        verified.IsWarning.ShouldBeTrue();
        verified.Source.ShouldBeSameAs(careers);
    }

    [Fact]
    public void A_discarded_claims_fabricated_url_is_logged_as_a_warning()
    {
        var sources = new[] { Source("https://acme.ai/blog") };
        var claims = new[] { Claim("https://acme.ai/press/fabricated") };

        var verifier = NewVerifier(out var logger);
        verifier.Verify(sources, claims);

        logger.Entries.ShouldContain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("https://acme.ai/press/fabricated", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unparseable_cited_url_is_discarded_not_thrown()
    {
        var sources = new[] { Source("https://acme.ai/blog") };
        var claims = new[] { Claim("not a url at all") };

        var result = NewVerifier(out _).Verify(sources, claims);

        result.Verified.ShouldBeEmpty();
        result.Discarded.ShouldBe(1);
    }
}
