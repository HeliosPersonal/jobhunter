using JobHunter.Domain.Companies;
using JobHunter.Domain.Intelligence;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Companies;

public sealed class CompanyTests
{
    private static readonly Guid CompanyId = Guid.Parse("00000000-0000-0000-0000-0000000000AA");
    private static readonly DateTimeOffset FirstSeen = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static CanonicalDomain Domain(string raw) => CanonicalDomain.TryCreate(raw).Value;

    private static BindingConfidence Confidence(decimal value) => BindingConfidence.TryCreate(value).Value;

    private static AtsBinding Binding(decimal confidence) =>
        new(Guid.NewGuid(), CompanyId, AtsKind.Greenhouse, "acme", Confidence(confidence), "{}", FirstSeen);

    private static Company NewCompany(bool isActive = true) =>
        new(CompanyId, Domain("acme.com"), "Acme", CompanySource.Curated, FirstSeen, isActive: isActive);

    [Fact]
    public void TryCreate_succeeds_and_seeds_seen_timestamps()
    {
        var result = Company.TryCreate(
            CompanyId, Domain("acme.com"), "Acme", CompanySource.Curated, FirstSeen, "https://acme.com/careers", "US");

        result.IsSuccess.ShouldBeTrue();
        var company = result.Value;
        company.CanonicalDomain.Value.ShouldBe("acme.com");
        company.DisplayName.ShouldBe("Acme");
        company.Source.ShouldBe(CompanySource.Curated);
        company.CareersUrl.ShouldBe("https://acme.com/careers");
        company.HqCountry.ShouldBe("US");
        company.IsActive.ShouldBeTrue();
        company.FirstSeenAt.ShouldBe(FirstSeen);
        company.LastSeenAt.ShouldBe(FirstSeen);
        company.Stage.ShouldBeNull();
        company.EmployeeBand.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryCreate_fails_for_a_blank_display_name(string name)
    {
        var result = Company.TryCreate(CompanyId, Domain("acme.com"), name, CompanySource.Curated, FirstSeen);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Company.BlankDisplayName);
    }

    [Fact]
    public void Constructor_rejects_a_blank_display_name()
    {
        Should.Throw<ArgumentException>(() =>
            new Company(CompanyId, Domain("acme.com"), "  ", CompanySource.Curated, FirstSeen));
    }

    [Fact]
    public void ActivateForDiscovery_refuses_without_a_confident_binding()
    {
        var company = NewCompany(isActive: false);

        var result = company.ActivateForDiscovery([Binding(0.79m)]);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Company.NoConfidentBinding);
        company.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void ActivateForDiscovery_refuses_with_no_bindings_at_all()
    {
        var company = NewCompany(isActive: false);

        company.ActivateForDiscovery([]).IsFailure.ShouldBeTrue();
        company.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void ActivateForDiscovery_ignores_a_retired_confident_binding()
    {
        var company = NewCompany(isActive: false);
        var retired = Binding(0.95m);
        retired.Retire(new FakeClock());

        company.ActivateForDiscovery([retired]).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void ActivateForDiscovery_succeeds_with_a_confident_live_binding()
    {
        var company = NewCompany(isActive: false);

        var result = company.ActivateForDiscovery([Binding(0.79m), Binding(0.80m)]);

        result.IsSuccess.ShouldBeTrue();
        company.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Touch_advances_last_seen_only_forwards()
    {
        var company = NewCompany();
        var clock = new FakeClock(FirstSeen);

        clock.Set(FirstSeen - TimeSpan.FromDays(1));
        company.Touch(clock);
        company.LastSeenAt.ShouldBe(FirstSeen);

        var later = FirstSeen + TimeSpan.FromDays(3);
        clock.Set(later);
        company.Touch(clock);
        company.LastSeenAt.ShouldBe(later);
    }

    [Fact]
    public void Retire_deactivates_and_is_idempotent()
    {
        var company = NewCompany();

        company.Retire();
        company.IsActive.ShouldBeFalse();

        company.Retire();
        company.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void ApplyFirmographics_records_the_first_observation()
    {
        var company = NewCompany();
        var observed = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

        var changed = company.ApplyFirmographics(CompanyStage.SeriesB, "51-200", observed);

        changed.ShouldBeTrue();
        company.Stage.ShouldBe("SeriesB");
        company.EmployeeBand.ShouldBe("51-200");
        company.FirmographicsObservedAt.ShouldBe(observed);
    }

    [Fact]
    public void ApplyFirmographics_ignores_both_nulls_and_reports_no_change()
    {
        var company = NewCompany();
        var observed = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

        var changed = company.ApplyFirmographics(null, null, observed);

        changed.ShouldBeFalse();
        company.Stage.ShouldBeNull();
        company.EmployeeBand.ShouldBeNull();
        company.FirmographicsObservedAt.ShouldBeNull();
    }

    [Fact]
    public void ApplyFirmographics_applies_only_the_supplied_field()
    {
        var company = NewCompany();
        var observed = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

        company.ApplyFirmographics(CompanyStage.Public, null, observed).ShouldBeTrue();

        company.Stage.ShouldBe("Public");
        company.EmployeeBand.ShouldBeNull();
        company.FirmographicsObservedAt.ShouldBe(observed);
    }

    [Fact]
    public void ApplyFirmographics_lets_a_newer_observation_overwrite_a_disagreement()
    {
        var company = NewCompany();
        var earlier = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var later = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);
        company.ApplyFirmographics(CompanyStage.SeriesB, "51-200", earlier);

        var changed = company.ApplyFirmographics(CompanyStage.SeriesC, "201-500", later);

        changed.ShouldBeTrue();
        company.Stage.ShouldBe("SeriesC");
        company.EmployeeBand.ShouldBe("201-500");
        company.FirmographicsObservedAt.ShouldBe(later);
    }

    [Fact]
    public void ApplyFirmographics_refuses_an_older_observation()
    {
        var company = NewCompany();
        var earlier = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var later = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);
        company.ApplyFirmographics(CompanyStage.SeriesC, "201-500", later);

        var changed = company.ApplyFirmographics(CompanyStage.SeriesB, "51-200", earlier);

        changed.ShouldBeFalse();
        company.Stage.ShouldBe("SeriesC");
        company.EmployeeBand.ShouldBe("201-500");
        company.FirmographicsObservedAt.ShouldBe(later);
    }

    [Fact]
    public void ApplyFirmographics_refuses_an_equally_old_observation()
    {
        var company = NewCompany();
        var observed = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);
        company.ApplyFirmographics(CompanyStage.SeriesC, "201-500", observed);

        var changed = company.ApplyFirmographics(CompanyStage.SeriesB, "51-200", observed);

        changed.ShouldBeFalse();
        company.Stage.ShouldBe("SeriesC");
        company.EmployeeBand.ShouldBe("201-500");
    }
}
