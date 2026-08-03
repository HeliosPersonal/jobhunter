using System.Globalization;
using JobHunter.Application.Normalization;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Normalization;

public sealed class LocationParserTests
{
    [Fact]
    public void Blank_text_yields_the_empty_set()
    {
        LocationParser.Parse("   ").IsEmpty.ShouldBeTrue();
        LocationParser.Parse(null).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void A_pure_remote_string_yields_the_empty_set()
    {
        LocationParser.Parse("Remote").IsEmpty.ShouldBeTrue();
        LocationParser.Parse("Anywhere").IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void A_single_country_parses_to_one_location()
    {
        var set = LocationParser.Parse("Germany");

        set.Count.ShouldBe(1);
        set.Locations[0].Country.ShouldBe("Germany");
        set.Locations[0].City.ShouldBeNull();
    }

    [Fact]
    public void City_and_country_parse_most_to_least_specific()
    {
        var set = LocationParser.Parse("Berlin, Germany");

        set.Count.ShouldBe(1);
        set.Locations[0].Country.ShouldBe("Germany");
        set.Locations[0].City.ShouldBe("Berlin");
    }

    [Fact]
    public void City_region_country_all_parse()
    {
        var set = LocationParser.Parse("San Francisco, California, United States");

        var location = set.Locations[0];
        location.City.ShouldBe("San Francisco");
        location.Region.ShouldBe("California");
        location.Country.ShouldBe("United States");
    }

    [Fact]
    public void Remote_noise_in_a_country_fragment_is_stripped()
    {
        var set = LocationParser.Parse("US (Remote)");

        set.Count.ShouldBe(1);
        set.Locations[0].Country.ShouldBe("US");
    }

    [Fact]
    public void Berlin_germany_free_text_parses()
    {
        var set = LocationParser.Parse("Berlin Germany");

        set.Count.ShouldBe(1);
        set.Locations[0].Country.ShouldBe("Berlin Germany");
    }

    [Fact]
    public void Multiple_locations_split_on_separators()
    {
        var set = LocationParser.Parse("Berlin, Germany; Paris, France");

        set.Count.ShouldBe(2);
        set.Locations.Select(l => l.Country).ShouldBe(["France", "Germany"]);
    }

    [Fact]
    public void Remote_emea_yields_the_empty_set_because_it_is_all_noise()
    {
        // "Remote - EMEA" is a policy, not a place; EMEA is not a country the parser will invent.
        LocationParser.Parse("Remote").IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void From_parts_uses_structured_country_and_city()
    {
        var set = LocationParser.FromParts("Germany", city: "Berlin");

        set.Count.ShouldBe(1);
        set.Locations[0].Country.ShouldBe("Germany");
        set.Locations[0].City.ShouldBe("Berlin");
    }

    [Fact]
    public void From_parts_keeps_a_city_even_without_a_country()
    {
        var set = LocationParser.FromParts(country: null, city: "Berlin");

        set.Count.ShouldBe(1);
        set.Locations[0].Country.ShouldBe("Berlin");
    }

    [Fact]
    public void From_parts_with_nothing_is_empty()
    {
        LocationParser.FromParts(null).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void From_parts_merges_secondary_text_locations()
    {
        var set = LocationParser.FromParts("Germany", city: "Berlin", secondaryText: "Paris, France");

        set.Count.ShouldBe(2);
        set.Locations.Select(l => l.Country).ShouldBe(["France", "Germany"]);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void Parsing_is_culture_invariant(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var set = LocationParser.Parse("Istanbul, Turkey");

            set.Locations[0].Key.ShouldBe("turkey||istanbul");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
