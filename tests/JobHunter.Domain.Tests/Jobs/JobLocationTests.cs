using System.Globalization;
using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Jobs;

public sealed class JobLocationTests
{
    [Fact]
    public void A_full_location_keeps_its_published_parts()
    {
        var location = JobLocation.TryCreate("United States", "California", "San Francisco").Value;

        location.Country.ShouldBe("United States");
        location.Region.ShouldBe("California");
        location.City.ShouldBe("San Francisco");
    }

    [Fact]
    public void A_country_only_location_leaves_the_rest_null()
    {
        var location = JobLocation.TryCreate("Germany").Value;

        location.Country.ShouldBe("Germany");
        location.Region.ShouldBeNull();
        location.City.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_country_is_a_failure(string? country)
    {
        var result = JobLocation.TryCreate(country, "Region", "City");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(JobLocation.Empty);
    }

    [Fact]
    public void Parts_are_trimmed_and_blank_optional_parts_become_null()
    {
        var location = JobLocation.TryCreate("  Germany  ", "  ", "  Berlin  ").Value;

        location.Country.ShouldBe("Germany");
        location.Region.ShouldBeNull();
        location.City.ShouldBe("Berlin");
    }

    [Fact]
    public void The_key_is_case_folded_and_culture_invariant()
    {
        var location = JobLocation.TryCreate("Germany", null, "Berlin").Value;

        location.Key.ShouldBe("germany||berlin");
    }

    [Fact]
    public void Equality_is_by_key_not_display_casing()
    {
        var a = JobLocation.TryCreate("Germany", null, "Berlin").Value;
        var b = JobLocation.TryCreate("GERMANY", null, "berlin").Value;

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Different_cities_are_not_equal()
    {
        var a = JobLocation.TryCreate("Germany", null, "Berlin").Value;
        var b = JobLocation.TryCreate("Germany", null, "Munich").Value;

        a.ShouldNotBe(b);
    }

    [Fact]
    public void The_key_does_not_depend_on_the_ambient_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // The Turkish "dotless i" is the classic culture-sensitive casing trap.
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var location = JobLocation.TryCreate("ISTANBUL", null, "Istanbul").Value;

            location.Key.ShouldBe("istanbul||istanbul");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ToString_reads_city_region_country()
    {
        JobLocation.TryCreate("United States", "California", "San Francisco").Value.ToString()
            .ShouldBe("San Francisco, California, United States");
        JobLocation.TryCreate("Germany").Value.ToString().ShouldBe("Germany");
    }
}
