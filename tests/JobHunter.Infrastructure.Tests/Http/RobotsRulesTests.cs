using JobHunter.Infrastructure.Http;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Http;

public sealed class RobotsRulesTests
{
    private const string UserAgent = "JobHunter/1.0 (+https://github.com/jobhunter/jobhunter; contact@x)";

    [Fact]
    public void Empty_body_allows_everything()
    {
        var rules = RobotsRules.Parse(string.Empty, UserAgent);

        rules.IsAllowed("/anything").ShouldBeTrue();
    }

    [Fact]
    public void A_wildcard_disallow_blocks_the_matching_prefix()
    {
        var rules = RobotsRules.Parse(
            """
            User-agent: *
            Disallow: /private
            """,
            UserAgent);

        rules.IsAllowed("/private/jobs").ShouldBeFalse();
        rules.IsAllowed("/public/jobs").ShouldBeTrue();
    }

    [Fact]
    public void A_disallow_all_blocks_the_root()
    {
        var rules = RobotsRules.Parse(
            """
            User-agent: *
            Disallow: /
            """,
            UserAgent);

        rules.IsAllowed("/").ShouldBeFalse();
        rules.IsAllowed("/jobs").ShouldBeFalse();
    }

    [Fact]
    public void An_empty_disallow_means_allow_all_for_the_group()
    {
        var rules = RobotsRules.Parse(
            """
            User-agent: *
            Disallow:
            """,
            UserAgent);

        rules.IsAllowed("/jobs").ShouldBeTrue();
    }

    [Fact]
    public void The_specific_agent_group_wins_over_the_wildcard()
    {
        var rules = RobotsRules.Parse(
            """
            User-agent: *
            Disallow: /

            User-agent: JobHunter
            Disallow: /admin
            """,
            UserAgent);

        rules.IsAllowed("/jobs").ShouldBeTrue();
        rules.IsAllowed("/admin/x").ShouldBeFalse();
    }

    [Fact]
    public void The_longest_matching_rule_wins()
    {
        var rules = RobotsRules.Parse(
            """
            User-agent: *
            Disallow: /a
            Allow: /a/b
            """,
            UserAgent);

        rules.IsAllowed("/a/x").ShouldBeFalse();
        rules.IsAllowed("/a/b/jobs").ShouldBeTrue();
    }

    [Fact]
    public void Allow_beats_disallow_on_an_equal_length_tie()
    {
        var rules = RobotsRules.Parse(
            """
            User-agent: *
            Disallow: /x
            Allow: /x
            """,
            UserAgent);

        rules.IsAllowed("/x/jobs").ShouldBeTrue();
    }

    [Fact]
    public void Comments_and_blank_lines_are_ignored()
    {
        var rules = RobotsRules.Parse(
            """
            # a comment
            User-agent: *   # trailing comment

            Disallow: /secret
            """,
            UserAgent);

        rules.IsAllowed("/secret/jobs").ShouldBeFalse();
        rules.IsAllowed("/open").ShouldBeTrue();
    }

    [Fact]
    public void AllowAll_and_DenyAll_are_the_permissive_and_conservative_readings()
    {
        RobotsRules.AllowAll.IsAllowed("/anything").ShouldBeTrue();
        RobotsRules.DenyAll.IsAllowed("/anything").ShouldBeFalse();
    }

    [Fact]
    public void A_body_with_no_group_allows_everything()
    {
        var rules = RobotsRules.Parse("Sitemap: https://x/sitemap.xml", UserAgent);

        rules.IsAllowed("/jobs").ShouldBeTrue();
    }
}
