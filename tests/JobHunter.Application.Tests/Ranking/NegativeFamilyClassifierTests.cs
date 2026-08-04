using JobHunter.Application.Ranking;
using JobHunter.Domain.Intelligence;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Ranking;

/// <summary>
/// T17: the pure negative role-family classifier (TUNE-06, match-schema §Suppression). It answers one question
/// from the role's <see cref="RoleFamily"/> and a configured negative set — is this a family the Owner is not
/// targeting? Every negative verdict carries a reason naming the family (invariant 4/11), so the down-weight or
/// suppression it drives is never a silent filter. Like the rest of the ranking chain it is static and pure, so
/// its verdict is provable (QG-3). The default negative set is the general off-target families
/// <c>{MlResearch, DataScience, PromptEng}</c>, deliberately disjoint from T15's narrow anti-goal predicate.
/// </summary>
public sealed class NegativeFamilyClassifierTests
{
    private static readonly IReadOnlySet<RoleFamily> DefaultNegative =
        RankingOptions.DefaultNegativeRoleFamilies;

    [Theory]
    [InlineData(RoleFamily.MlResearch)]
    [InlineData(RoleFamily.DataScience)]
    [InlineData(RoleFamily.PromptEng)]
    public void A_role_in_the_default_negative_set_is_flagged_with_a_family_naming_reason(RoleFamily roleFamily)
    {
        var verdict = NegativeFamilyClassifier.Classify(roleFamily, DefaultNegative);

        verdict.IsNegative.ShouldBeTrue();
        verdict.Reason.ShouldBe($"Not a target role family: {roleFamily}");
    }

    [Theory]
    [InlineData(RoleFamily.AiPlatform)]
    [InlineData(RoleFamily.Platform)]
    [InlineData(RoleFamily.AiApplications)]
    [InlineData(RoleFamily.ForwardDeployed)]
    [InlineData(RoleFamily.FoundingEng)]
    [InlineData(RoleFamily.BackendGeneric)]
    [InlineData(RoleFamily.Fullstack)]
    [InlineData(RoleFamily.DevOpsSRE)]
    [InlineData(RoleFamily.Frontend)]
    [InlineData(RoleFamily.Other)]
    public void A_target_or_neutral_family_is_not_negative_under_the_default_set(RoleFamily roleFamily)
    {
        var verdict = NegativeFamilyClassifier.Classify(roleFamily, DefaultNegative);

        verdict.IsNegative.ShouldBeFalse();
        verdict.Reason.ShouldBeNull();
    }

    [Fact]
    public void The_enterprise_crud_family_is_not_in_the_default_negative_set()
    {
        // T15 owns EnterpriseCrud through its narrow anti-goal predicate; T17's default set is disjoint from it,
        // so the two penalties never double-fire on the same role under the defaults.
        DefaultNegative.ShouldNotContain(RoleFamily.EnterpriseCrud);

        NegativeFamilyClassifier.Classify(RoleFamily.EnterpriseCrud, DefaultNegative).IsNegative.ShouldBeFalse();
    }

    [Fact]
    public void The_negative_set_is_config_driven_so_a_widened_set_flags_more_families()
    {
        // The Owner can widen the set without a code change: add EnterpriseCrud and it becomes negative here too.
        var widened = new HashSet<RoleFamily>(DefaultNegative) { RoleFamily.EnterpriseCrud };

        var verdict = NegativeFamilyClassifier.Classify(RoleFamily.EnterpriseCrud, widened);

        verdict.IsNegative.ShouldBeTrue();
        verdict.Reason.ShouldBe("Not a target role family: EnterpriseCrud");
    }

    [Fact]
    public void An_empty_negative_set_makes_every_family_non_negative()
    {
        // Opting the filter off entirely: no family is negative, so nothing is down-weighted or suppressed by T17.
        var off = new HashSet<RoleFamily>();

        foreach (var family in Enum.GetValues<RoleFamily>())
        {
            NegativeFamilyClassifier.Classify(family, off).IsNegative.ShouldBeFalse();
        }
    }

    [Fact]
    public void A_null_negative_set_is_a_programmer_error()
    {
        Should.Throw<ArgumentNullException>(
            () => NegativeFamilyClassifier.Classify(RoleFamily.MlResearch, null!));
    }
}
