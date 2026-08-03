using JobHunter.Telegram.Search;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests;

/// <summary>
/// The inline filter syntax of <c>/search &lt;query&gt;</c> (F9-T09, DoD "filters expressed inline in a
/// simple documented syntax"). Recognised <c>key:value</c> tokens become typed filters; everything else is
/// free text; and parsing is total — a malformed or unknown token falls through to free text rather than
/// failing the search. The command builds the same typed <see cref="Domain.Search.SearchQuery"/> the API
/// binds, so user input never becomes a filter operator (AC-02).
/// </summary>
public sealed class SearchCommandParserTests
{
    [Fact]
    public void A_bare_query_is_all_free_text_and_capped_at_ten()
    {
        var query = SearchCommandParser.Parse("staff site reliability engineer");

        query.Text.ShouldBe("staff site reliability engineer");
        query.Technologies.ShouldBeEmpty();
        query.Limit.ShouldBe(10);
    }

    [Fact]
    public void Recognised_filters_become_typed_parameters_and_leave_the_free_text()
    {
        var query = SearchCommandParser.Parse(
            "platform engineer tech:go remote:remote country:Germany seniority:Senior stage:SeriesB min-salary:120000");

        query.Text.ShouldBe("platform engineer");
        query.Technologies.ShouldBe(["go"]);
        query.RemotePolicies.ShouldBe(["remote"]);
        query.Countries.ShouldBe(["Germany"]);
        query.Seniorities.ShouldBe(["Senior"]);
        query.CompanyStages.ShouldBe(["SeriesB"]);
        query.SalaryMin.ShouldBe(120000);
    }

    [Fact]
    public void A_repeated_filter_key_widens_the_set()
    {
        var query = SearchCommandParser.Parse("tech:go tech:rust");

        query.Technologies.ShouldBe(["go", "rust"]);
        query.Text.ShouldBeEmpty();
    }

    [Fact]
    public void Filter_keys_are_case_insensitive()
    {
        var query = SearchCommandParser.Parse("Tech:Kafka");

        query.Technologies.ShouldBe(["Kafka"]);
    }

    [Fact]
    public void An_unknown_key_is_treated_as_free_text_not_an_error()
    {
        var query = SearchCommandParser.Parse("salary:high ratio:1:1");

        query.Text.ShouldBe("salary:high ratio:1:1");
        query.SalaryMin.ShouldBeNull();
    }

    [Fact]
    public void A_non_numeric_min_salary_falls_through_to_free_text()
    {
        var query = SearchCommandParser.Parse("min-salary:lots");

        query.SalaryMin.ShouldBeNull();
        query.Text.ShouldBe("min-salary:lots");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_argument_is_a_valid_filters_only_query(string? arguments)
    {
        var query = SearchCommandParser.Parse(arguments);

        query.Text.ShouldBeEmpty();
        query.Technologies.ShouldBeEmpty();
        query.SalaryMin.ShouldBeNull();
    }

    [Fact]
    public void A_trailing_colon_with_no_value_is_free_text()
    {
        var query = SearchCommandParser.Parse("tech:");

        query.Technologies.ShouldBeEmpty();
        query.Text.ShouldBe("tech:");
    }
}
