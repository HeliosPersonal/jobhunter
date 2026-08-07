using JobHunter.Domain.Research;
using JobHunter.Infrastructure.Http;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Http;

/// <summary>
/// The category host allowlist (SAD §8 Allowlist, QG-3, risk D1). A research target host must match a
/// category-specific pattern, or it is refused — the second half of the SSRF defence beside the
/// public-address check. Company-scoped categories permit only the company's own registrable domain and its
/// subdomains; third-party categories permit a fixed set of public hosts (e.g. GitHub for open source).
/// Matching is on a dot boundary, so <c>evil-github.com</c> never matches <c>github.com</c>.
/// </summary>
public sealed class ResearchHostAllowlistTests
{
    private const string Company = "stripe.com";

    private static bool Allowed(ResearchCategory category, string url, string company = Company) =>
        ResearchHostAllowlist.IsAllowed(category, new Uri(url), company);

    [Theory]
    [InlineData(ResearchCategory.EngineeringBlog, "https://stripe.com/blog/engineering")]
    [InlineData(ResearchCategory.EngineeringBlog, "https://blog.stripe.com/scaling")]
    [InlineData(ResearchCategory.Stack, "https://stripe.com/jobs")]
    [InlineData(ResearchCategory.InterviewProcess, "https://careers.stripe.com/process")]
    public void A_company_scoped_category_permits_the_company_domain_and_subdomains(
        ResearchCategory category, string url)
    {
        Allowed(category, url).ShouldBeTrue();
    }

    [Theory]
    [InlineData(ResearchCategory.EngineeringBlog, "https://medium.com/@stripe/scaling")]
    [InlineData(ResearchCategory.Stack, "https://competitor.com/stripe")]
    [InlineData(ResearchCategory.InterviewProcess, "https://stripe.com.evil.test/process")]
    public void A_company_scoped_category_refuses_any_other_host(ResearchCategory category, string url)
    {
        Allowed(category, url).ShouldBeFalse();
    }

    [Theory]
    [InlineData("https://api.github.com/orgs/stripe/repos")]
    [InlineData("https://github.com/stripe")]
    public void Open_source_permits_github(string url)
    {
        Allowed(ResearchCategory.OpenSource, url).ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://gitlab-github.com/stripe")]
    [InlineData("https://evil-github.com/stripe")]
    [InlineData("https://stripe.com/repos")]
    public void Open_source_refuses_a_non_github_host_even_the_company_domain(string url)
    {
        Allowed(ResearchCategory.OpenSource, url).ShouldBeFalse();
    }

    [Fact]
    public void A_subdomain_match_is_on_a_dot_boundary_not_a_suffix()
    {
        // "notgithub.com" ends with "github.com" as a raw string but is a different registrable domain.
        ResearchHostAllowlist.IsAllowed(
            ResearchCategory.OpenSource, new Uri("https://notgithub.com/stripe"), Company).ShouldBeFalse();
    }

    [Fact]
    public void A_blank_company_domain_refuses_a_company_scoped_category()
    {
        Allowed(ResearchCategory.EngineeringBlog, "https://stripe.com/blog", company: "").ShouldBeFalse();
    }

    [Fact]
    public void The_company_domain_match_is_case_insensitive()
    {
        Allowed(ResearchCategory.EngineeringBlog, "https://BLOG.Stripe.COM/x").ShouldBeTrue();
    }
}
