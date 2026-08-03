using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Jobs;

public sealed class LocationSetTests
{
    private static JobLocation Loc(string country, string? region = null, string? city = null) =>
        JobLocation.TryCreate(country, region, city).Value;

    [Fact]
    public void The_empty_set_is_empty()
    {
        LocationSet.Empty.IsEmpty.ShouldBeTrue();
        LocationSet.Empty.Count.ShouldBe(0);
        LocationSet.Empty.SortedKey.ShouldBe(string.Empty);
    }

    [Fact]
    public void An_empty_sequence_yields_an_empty_set()
    {
        LocationSet.Of([]).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Equality_is_order_insensitive()
    {
        var a = LocationSet.Of([Loc("Germany", city: "Berlin"), Loc("France", city: "Paris")]);
        var b = LocationSet.Of([Loc("France", city: "Paris"), Loc("Germany", city: "Berlin")]);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
        a.SortedKey.ShouldBe(b.SortedKey);
    }

    [Fact]
    public void The_sorted_key_is_deterministic_and_ordinal()
    {
        var set = LocationSet.Of([Loc("Germany", city: "Berlin"), Loc("France", city: "Paris")]);

        set.SortedKey.ShouldBe("france||paris\ngermany||berlin");
    }

    [Fact]
    public void Duplicate_locations_are_collapsed()
    {
        var set = LocationSet.Of([Loc("Germany", city: "Berlin"), Loc("GERMANY", city: "berlin")]);

        set.Count.ShouldBe(1);
    }

    [Fact]
    public void Different_sets_are_not_equal()
    {
        var a = LocationSet.Of([Loc("Germany", city: "Berlin")]);
        var b = LocationSet.Of([Loc("Germany", city: "Munich")]);

        a.ShouldNotBe(b);
    }

    [Fact]
    public void An_empty_set_is_not_equal_to_a_populated_one()
    {
        LocationSet.Empty.ShouldNotBe(LocationSet.Of([Loc("Germany")]));
    }

    [Fact]
    public void Locations_are_exposed_in_key_order()
    {
        var set = LocationSet.Of([Loc("Germany", city: "Berlin"), Loc("France", city: "Paris")]);

        set.Locations.Select(l => l.Country).ShouldBe(["France", "Germany"]);
    }

    [Fact]
    public void A_null_sequence_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => LocationSet.Of(null!));
    }

    [Fact]
    public void A_null_entry_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => LocationSet.Of([null!]));
    }
}
