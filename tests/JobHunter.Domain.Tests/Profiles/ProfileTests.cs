using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Profiles;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Profiles;

public sealed class ProfileTests
{
    private static readonly Guid ProfileId = Guid.Parse("00000000-0000-0000-0000-0000000000F1");

    private static Profile NewProfile(
        bool isActive = true,
        string displayName = "Owner",
        decimal? salaryFloor = null,
        string? salaryFloorCurrency = null,
        TimezoneBand timezoneBand = TimezoneBand.EMEA,
        IReadOnlyList<string>? preferredCountries = null,
        IReadOnlyList<EmploymentType>? employmentTypes = null,
        IReadOnlyList<RoleFamily>? targetRoleFamilies = null,
        AiUsageLevel? desiredAiUsageFloor = null,
        IReadOnlyList<string>? targetTitles = null)
    {
        var clock = new FakeClock();
        return new Profile(
            ProfileId,
            isActive,
            displayName,
            salaryFloor,
            salaryFloorCurrency,
            timezoneBand,
            preferredCountries ?? ["Ukraine", "Poland"],
            employmentTypes ?? [EmploymentType.FullTime, EmploymentType.Contract],
            clock.UtcNow,
            targetRoleFamilies,
            desiredAiUsageFloor,
            targetTitles);
    }

    [Fact]
    public void A_valid_profile_exposes_its_fields()
    {
        var profile = NewProfile(salaryFloor: 90000m, salaryFloorCurrency: "eur");

        profile.Id.ShouldBe(ProfileId);
        profile.IsActive.ShouldBeTrue();
        profile.DisplayName.ShouldBe("Owner");
        profile.SalaryFloor.ShouldBe(90000m);
        profile.SalaryFloorCurrency.ShouldBe("EUR");
        profile.TimezoneBand.ShouldBe(TimezoneBand.EMEA);
        profile.PreferredCountries.ShouldBe(["Ukraine", "Poland"]);
        profile.EmploymentTypes.ShouldBe([EmploymentType.FullTime, EmploymentType.Contract]);
    }

    [Fact]
    public void A_blank_display_name_is_rejected()
    {
        Should.Throw<ArgumentException>(() => NewProfile(displayName: "  "));
    }

    [Fact]
    public void An_empty_id_is_rejected()
    {
        var clock = new FakeClock();
        Should.Throw<ArgumentException>(() => new Profile(
            Guid.Empty, true, "Owner", null, null, TimezoneBand.EMEA, [], [], clock.UtcNow));
    }

    [Fact]
    public void A_salary_floor_amount_without_a_currency_is_rejected()
    {
        Should.Throw<ArgumentException>(() => NewProfile(salaryFloor: 90000m, salaryFloorCurrency: null));
    }

    [Fact]
    public void A_salary_floor_currency_without_an_amount_is_rejected()
    {
        Should.Throw<ArgumentException>(() => NewProfile(salaryFloor: null, salaryFloorCurrency: "EUR"));
    }

    [Fact]
    public void A_negative_salary_floor_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NewProfile(salaryFloor: -1m, salaryFloorCurrency: "EUR"));
    }

    [Fact]
    public void A_malformed_salary_floor_currency_is_rejected()
    {
        Should.Throw<ArgumentException>(() => NewProfile(salaryFloor: 90000m, salaryFloorCurrency: "EU"));
    }

    [Fact]
    public void No_salary_floor_leaves_both_null()
    {
        var profile = NewProfile();

        profile.SalaryFloor.ShouldBeNull();
        profile.SalaryFloorCurrency.ShouldBeNull();
    }

    [Fact]
    public void Preferred_countries_are_trimmed_deblanked_and_deduplicated()
    {
        var profile = NewProfile(preferredCountries: ["  Ukraine ", "", "ukraine", "Poland"]);

        profile.PreferredCountries.Count.ShouldBe(2);
        profile.PreferredCountries.ShouldContain("Ukraine");
        profile.PreferredCountries.ShouldContain("Poland");
    }

    [Fact]
    public void Duplicate_employment_types_are_collapsed()
    {
        var profile = NewProfile(employmentTypes: [EmploymentType.FullTime, EmploymentType.FullTime]);

        profile.EmploymentTypes.Count.ShouldBe(1);
    }

    [Fact]
    public void Exposed_collections_are_read_only_copies()
    {
        var profile = NewProfile();

        profile.PreferredCountries.ShouldBeAssignableTo<IReadOnlyList<string>>();
        profile.EmploymentTypes.ShouldBeAssignableTo<IReadOnlyList<EmploymentType>>();
    }

    // ---- T16: the Owner's career goal (target families, desired AI-usage floor, target titles) ----

    [Fact]
    public void A_profile_without_a_stated_goal_defaults_to_no_goal()
    {
        var profile = NewProfile();

        profile.TargetRoleFamilies.ShouldBeEmpty();
        profile.DesiredAiUsageFloor.ShouldBeNull();
        profile.TargetTitles.ShouldBeEmpty();
    }

    [Fact]
    public void A_stated_goal_is_exposed()
    {
        var profile = NewProfile(
            targetRoleFamilies: [RoleFamily.AiPlatform, RoleFamily.Platform],
            desiredAiUsageFloor: AiUsageLevel.Medium,
            targetTitles: ["Staff Platform Engineer", "AI Platform Engineer"]);

        profile.TargetRoleFamilies.ShouldBe([RoleFamily.AiPlatform, RoleFamily.Platform]);
        profile.DesiredAiUsageFloor.ShouldBe(AiUsageLevel.Medium);
        profile.TargetTitles.ShouldBe(["Staff Platform Engineer", "AI Platform Engineer"]);
    }

    [Fact]
    public void Duplicate_target_role_families_are_collapsed()
    {
        var profile = NewProfile(
            targetRoleFamilies: [RoleFamily.AiPlatform, RoleFamily.AiPlatform, RoleFamily.Platform]);

        profile.TargetRoleFamilies.ShouldBe([RoleFamily.AiPlatform, RoleFamily.Platform]);
    }

    [Fact]
    public void Target_titles_are_trimmed_deblanked_and_deduplicated()
    {
        var profile = NewProfile(targetTitles: ["  Staff Engineer ", "", "staff engineer", "Platform Lead"]);

        profile.TargetTitles.Count.ShouldBe(2);
        profile.TargetTitles.ShouldContain("Staff Engineer");
        profile.TargetTitles.ShouldContain("Platform Lead");
    }

    [Fact]
    public void An_unknown_desired_ai_usage_floor_is_rejected()
    {
        // Unknown is the tolerant parser's sentinel, never a level the Owner would deliberately target.
        Should.Throw<ArgumentException>(() => NewProfile(desiredAiUsageFloor: AiUsageLevel.Unknown));
    }

    // ---- F10 T08: /floor sets the explicit salary floor on the Profile ----

    [Fact]
    public void Setting_the_salary_floor_records_the_amount_currency_and_touch_time()
    {
        var profile = NewProfile();
        var when = new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

        profile.SetSalaryFloor(120000m, "eur", when);

        profile.SalaryFloor.ShouldBe(120000m);
        profile.SalaryFloorCurrency.ShouldBe("EUR");
        profile.UpdatedAt.ShouldBe(when);
    }

    [Fact]
    public void Setting_the_salary_floor_overwrites_a_previous_one()
    {
        var profile = NewProfile(salaryFloor: 90000m, salaryFloorCurrency: "USD");

        profile.SetSalaryFloor(150000m, "USD", DateTimeOffset.UtcNow);

        profile.SalaryFloor.ShouldBe(150000m);
        profile.SalaryFloorCurrency.ShouldBe("USD");
    }

    [Fact]
    public void A_negative_floor_amount_is_rejected_by_the_mutator()
    {
        var profile = NewProfile();

        Should.Throw<ArgumentOutOfRangeException>(() => profile.SetSalaryFloor(-1m, "EUR", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void A_blank_floor_currency_is_rejected_by_the_mutator()
    {
        var profile = NewProfile();

        Should.Throw<ArgumentException>(() => profile.SetSalaryFloor(120000m, "  ", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void A_malformed_floor_currency_is_rejected_by_the_mutator()
    {
        var profile = NewProfile();

        Should.Throw<ArgumentException>(() => profile.SetSalaryFloor(120000m, "EU", DateTimeOffset.UtcNow));
    }
}
