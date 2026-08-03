using JobHunter.Application.Normalization;
using JobHunter.Infrastructure.Normalization;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Normalization;

/// <summary>
/// T11: the committed title-tier reference config loads from the embedded YAML into a validated config —
/// the load that runs at host startup, so a malformed reference or one that drifts from the F3 RoleFamily
/// vocabulary fails here rather than at run time. The config carries no scoring logic; it is a reviewable
/// mapping from the Owner's Tier-1/2/3 target titles onto role-family archetypes (review §5).
/// </summary>
public sealed class TitleTierConfigLoaderTests
{
    [Fact]
    public void The_embedded_config_loads_and_covers_every_review_title()
    {
        var config = TitleTierConfigLoader.Load();

        // Review §5 lists 5 Tier-1, 8 Tier-2 and 4 Tier-3 titles.
        config.Count.ShouldBe(17);
        config.Entries.Count(e => e.Tier == TitleTier.Tier1).ShouldBe(5);
        config.Entries.Count(e => e.Tier == TitleTier.Tier2).ShouldBe(8);
        config.Entries.Count(e => e.Tier == TitleTier.Tier3).ShouldBe(4);
    }

    [Fact]
    public void Every_role_family_archetype_is_a_known_F3_role_family()
    {
        // The reference and the F3 RoleFamily vocabulary must never drift apart: every archetype the config
        // maps a title onto is a member of the canonical set.
        var config = TitleTierConfigLoader.Load();

        config.RoleFamilies.ShouldBeSubsetOf(RoleFamilyArchetypes.All);
    }

    [Theory]
    [InlineData("AI Platform Engineer", "AiPlatform")]
    [InlineData("Forward Deployed Engineer", "ForwardDeployed")]
    [InlineData("Founding Engineer (AI)", "FoundingEng")]
    [InlineData("Applied AI Engineer", "AiApplications")]
    [InlineData("Senior Software Engineer", "BackendGeneric")]
    public void An_anchor_title_maps_to_its_expected_archetype(string title, string archetype)
    {
        var entry = TitleTierConfigLoader.Load().Entries
            .Single(e => string.Equals(e.Title, title, StringComparison.Ordinal));

        entry.RoleFamily.ShouldBe(archetype);
    }

    [Fact]
    public void A_non_sequence_document_is_a_named_failure()
    {
        var ex = Should.Throw<TitleTierConfigException>(() =>
            TitleTierConfigLoader.Parse("tier: Tier1"));

        ex.Message.ShouldContain("sequence");
    }

    [Fact]
    public void An_entry_with_no_title_is_a_named_failure()
    {
        const string yaml = """
            - tier: Tier1
              role_family: AiPlatform
            """;

        var ex = Should.Throw<TitleTierConfigException>(() => TitleTierConfigLoader.Parse(yaml));

        ex.Message.ShouldContain("line 1");
        ex.Message.ShouldContain("title");
    }

    [Fact]
    public void An_unknown_tier_fails_naming_the_line_and_the_allowed_values()
    {
        const string yaml = """
            - tier: Tier9
              title: Whatever Engineer
              role_family: AiPlatform
            """;

        var ex = Should.Throw<TitleTierConfigException>(() => TitleTierConfigLoader.Parse(yaml));

        ex.Message.ShouldContain("line 1");
        ex.Message.ShouldContain("Tier9");
        ex.Message.ShouldContain("Tier1");
    }

    [Fact]
    public void An_unknown_role_family_is_a_named_failure()
    {
        const string yaml = """
            - tier: Tier1
              title: AI Platform Engineer
              role_family: NotARealFamily
            """;

        var ex = Should.Throw<TitleTierConfigException>(() => TitleTierConfigLoader.Parse(yaml));

        ex.Message.ShouldContain("NotARealFamily");
    }

    [Fact]
    public void A_duplicated_title_is_a_named_failure()
    {
        const string yaml = """
            - tier: Tier1
              title: AI Platform Engineer
              role_family: AiPlatform
            - tier: Tier2
              title: AI Platform Engineer
              role_family: Platform
            """;

        var ex = Should.Throw<TitleTierConfigException>(() => TitleTierConfigLoader.Parse(yaml));

        ex.Message.ShouldContain("AI Platform Engineer");
    }

    [Fact]
    public void An_empty_document_yields_an_empty_config()
    {
        TitleTierConfigLoader.Parse(string.Empty).Count.ShouldBe(0);
    }

    [Fact]
    public void A_non_mapping_sequence_item_is_a_named_failure()
    {
        var ex = Should.Throw<TitleTierConfigException>(() => TitleTierConfigLoader.Parse("- just-a-scalar"));

        ex.Message.ShouldContain("not a mapping");
    }

    [Fact]
    public void Malformed_yaml_fails_as_a_config_exception_not_a_raw_parse_error()
    {
        var ex = Should.Throw<TitleTierConfigException>(() =>
            TitleTierConfigLoader.Parse("- { tier: Tier1"));

        ex.Message.ShouldContain("not valid YAML");
    }

    [Fact]
    public void Parse_rejects_a_null_argument()
    {
        Should.Throw<ArgumentNullException>(() => TitleTierConfigLoader.Parse(null!));
    }
}
