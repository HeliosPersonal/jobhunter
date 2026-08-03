using JobHunter.Application.Normalization;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Normalization;

/// <summary>
/// T11: the pure title-tier reference config. It maps the Owner's target titles onto role-family archetypes
/// and validates on construction — a blank or duplicated title, or a role family outside the F3 vocabulary,
/// is rejected so the reference can never drift from the classifier it feeds. There is no scoring logic
/// here; construction is a pure function of its entries (review §5, TUNE-12).
/// </summary>
public sealed class TitleTierConfigTests
{
    private static readonly TitleTierEntry[] Entries =
    [
        new(TitleTier.Tier1, "AI Platform Engineer", "AiPlatform"),
        new(TitleTier.Tier2, "Platform Engineer", "Platform"),
        new(TitleTier.Tier3, "Senior Software Engineer", "BackendGeneric"),
    ];

    [Fact]
    public void A_well_formed_config_exposes_its_entries_in_file_order()
    {
        var config = new TitleTierConfig(Entries);

        config.Count.ShouldBe(3);
        config.Entries.Select(e => e.Title)
            .ShouldBe(["AI Platform Engineer", "Platform Engineer", "Senior Software Engineer"]);
    }

    [Fact]
    public void The_distinct_role_families_are_a_subset_of_the_F3_vocabulary()
    {
        new TitleTierConfig(Entries).RoleFamilies.ShouldBeSubsetOf(RoleFamilyArchetypes.All);
    }

    [Fact]
    public void A_title_is_trimmed_on_construction()
    {
        var config = new TitleTierConfig([new(TitleTier.Tier1, "  AI Platform Engineer  ", "AiPlatform")]);

        config.Entries[0].Title.ShouldBe("AI Platform Engineer");
    }

    [Fact]
    public void A_blank_title_is_rejected_at_construction()
    {
        TitleTierEntry[] entries = [new(TitleTier.Tier1, "   ", "AiPlatform")];

        Should.Throw<ArgumentException>(() => new TitleTierConfig(entries));
    }

    [Fact]
    public void A_duplicated_title_is_rejected_at_construction()
    {
        TitleTierEntry[] entries =
        [
            new(TitleTier.Tier1, "AI Platform Engineer", "AiPlatform"),
            new(TitleTier.Tier2, "ai platform engineer", "Platform"),
        ];

        var ex = Should.Throw<ArgumentException>(() => new TitleTierConfig(entries));
        ex.Message.ShouldContain("AI Platform Engineer");
    }

    [Fact]
    public void An_unknown_role_family_is_rejected_at_construction()
    {
        TitleTierEntry[] entries = [new(TitleTier.Tier1, "AI Platform Engineer", "NotARealFamily")];

        var ex = Should.Throw<ArgumentException>(() => new TitleTierConfig(entries));
        ex.Message.ShouldContain("NotARealFamily");
    }

    [Fact]
    public void A_null_entry_is_rejected()
    {
        TitleTierEntry[] entries = [null!];

        Should.Throw<ArgumentNullException>(() => new TitleTierConfig(entries));
    }

    [Fact]
    public void A_null_argument_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => new TitleTierConfig(null!));
    }

    [Fact]
    public void Every_documented_role_family_archetype_is_recognised()
    {
        // Guards the canonical set against an accidental deletion — the F3 RoleFamily enum documents exactly
        // these fourteen archetypes (enrichment-schema §RoleFamily).
        RoleFamilyArchetypes.All.Count.ShouldBe(14);
        RoleFamilyArchetypes.All.ShouldContain("AiPlatform");
        RoleFamilyArchetypes.All.ShouldContain("EnterpriseCrud");
        RoleFamilyArchetypes.All.ShouldContain("Other");
    }
}
