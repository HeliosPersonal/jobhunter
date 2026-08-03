using JobHunter.Api.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Shouldly;
using Xunit;

namespace JobHunter.Api.Tests;

/// <summary>
/// The query-string binding is a pure function of the query collection (T05). A user term becomes a typed
/// parameter and never a filter operator (AC-02); repeated keys become a list; and an unparseable number
/// or flag degrades to its default rather than failing the request.
/// </summary>
public sealed class SearchQueryBindingTests
{
    private static QueryCollection Query(params (string Key, string[] Values)[] pairs)
    {
        var dict = new Dictionary<string, StringValues>(StringComparer.Ordinal);
        foreach (var (key, values) in pairs)
        {
            dict[key] = new StringValues(values);
        }

        return new QueryCollection(dict);
    }

    [Fact]
    public void A_bare_text_query_binds_the_free_text_and_leaves_filters_empty()
    {
        var result = SearchQueryBinding.FromQuery(Query(("q", ["kafka distributed"])));

        result.Text.ShouldBe("kafka distributed");
        result.Technologies.ShouldBeEmpty();
        result.CompanyStages.ShouldBeEmpty();
        result.MinScore.ShouldBeNull();
        result.SalaryMin.ShouldBeNull();
        result.IncludeClosed.ShouldBeFalse();
        result.Limit.ShouldBe(20);
        result.Cursor.ShouldBeNull();
    }

    [Fact]
    public void Repeated_filter_keys_bind_to_a_list()
    {
        var result = SearchQueryBinding.FromQuery(Query(
            ("technology", ["Kafka", "Azure"]),
            ("companyStage", ["SeriesB", "SeriesC"]),
            ("country", ["DE", "NL"]),
            ("remotePolicy", ["Remote"]),
            ("seniority", ["Staff"])));

        result.Technologies.ShouldBe(["Kafka", "Azure"]);
        result.CompanyStages.ShouldBe(["SeriesB", "SeriesC"]);
        result.Countries.ShouldBe(["DE", "NL"]);
        result.RemotePolicies.ShouldBe(["Remote"]);
        result.Seniorities.ShouldBe(["Staff"]);
    }

    [Fact]
    public void Blank_filter_values_are_dropped()
    {
        var result = SearchQueryBinding.FromQuery(Query(("technology", ["Kafka", "  ", ""])));

        result.Technologies.ShouldBe(["Kafka"]);
    }

    [Fact]
    public void Numeric_and_flag_parameters_bind_when_well_formed()
    {
        var result = SearchQueryBinding.FromQuery(Query(
            ("minScore", ["70.5"]),
            ("salaryMin", ["150000"]),
            ("includeClosed", ["true"]),
            ("limit", ["50"]),
            ("cursor", ["abc123"])));

        result.MinScore.ShouldBe(70.5);
        result.SalaryMin.ShouldBe(150000);
        result.IncludeClosed.ShouldBeTrue();
        result.Limit.ShouldBe(50);
        result.Cursor.ShouldBe("abc123");
    }

    [Fact]
    public void An_unparseable_number_degrades_to_its_default_rather_than_failing()
    {
        var result = SearchQueryBinding.FromQuery(Query(
            ("minScore", ["abc"]),
            ("salaryMin", ["not-a-number"]),
            ("limit", ["oops"]),
            ("includeClosed", ["maybe"])));

        result.MinScore.ShouldBeNull();
        result.SalaryMin.ShouldBeNull();
        result.Limit.ShouldBe(20);
        result.IncludeClosed.ShouldBeFalse();
    }

    [Fact]
    public void A_blank_cursor_binds_as_null()
    {
        var result = SearchQueryBinding.FromQuery(Query(("cursor", ["   "])));

        result.Cursor.ShouldBeNull();
    }

    [Fact]
    public void A_null_repeated_value_is_dropped_alongside_the_real_ones()
    {
        // StringValues can carry a null entry; the binder treats it as blank and drops it.
        var query = new QueryCollection(new Dictionary<string, StringValues>(StringComparer.Ordinal)
        {
            ["technology"] = new StringValues([null, "Kafka"]),
        });

        var result = SearchQueryBinding.FromQuery(query);

        result.Technologies.ShouldBe(["Kafka"]);
    }
}
