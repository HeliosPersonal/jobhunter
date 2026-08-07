using JobHunter.Domain.Search;
using JobHunter.Search;
using Shouldly;
using Xunit;

namespace JobHunter.Search.Tests;

/// <summary>
/// The filter builder (F9-T03) — the injection defence (AC-02). Every clause is built from a typed
/// parameter and every user term is backtick-escaped, so a term that contains Typesense filter syntax is
/// matched as literal text and can never change the shape of the expression. Also: closed jobs are
/// excluded unless asked for (AC-08), and a bare full-text query produces no filter at all.
/// <see cref="SearchFilterBuilder"/> is internal, reached through <c>InternalsVisibleTo</c>.
/// </summary>
public sealed class SearchFilterBuilderTests
{
    [Fact]
    public void A_bare_query_filters_only_to_live_jobs()
    {
        var filter = SearchFilterBuilder.Build(new SearchQuery { Text = "kafka" });

        filter.ShouldBe("status:=`Live`");
    }

    [Fact]
    public void Include_closed_drops_the_live_only_clause_entirely()
    {
        var filter = SearchFilterBuilder.Build(new SearchQuery { IncludeClosed = true });

        filter.ShouldBeNull();
    }

    [Fact]
    public void Typed_string_sets_become_escaped_or_clauses()
    {
        var filter = SearchFilterBuilder.Build(new SearchQuery
        {
            Technologies = ["Kafka", "Azure"],
            Countries = ["DE"],
        });

        filter.ShouldNotBeNull();
        filter.ShouldContain("technologies:=[`Kafka`,`Azure`]");
        filter.ShouldContain("countries:=[`DE`]");
        filter.ShouldContain("status:=`Live`");
        filter.ShouldContain(" && ");
    }

    [Fact]
    public void A_term_that_is_filter_syntax_is_escaped_and_treated_as_a_literal_value()
    {
        // The classic injection attempt: a value that tries to open its own clause.
        var filter = SearchFilterBuilder.Build(new SearchQuery
        {
            Technologies = ["Kafka` || score:>0 && `x"],
        });

        // The embedded backticks are stripped, so the whole thing stays one balanced backtick token —
        // it cannot break out into a new operator.
        filter.ShouldNotBeNull();
        filter.ShouldContain("technologies:=[`Kafka || score:>0 && x`]");
        // Exactly two backticks were added around the (sanitised) value: no stray delimiter survived.
        filter!.Count(c => c == '`').ShouldBe(2 + 2); // two for status:=`Live`, two for the value
    }

    [Fact]
    public void Numeric_thresholds_become_range_clauses()
    {
        var filter = SearchFilterBuilder.Build(new SearchQuery { MinScore = 70.5, SalaryMin = 150000 });

        filter.ShouldNotBeNull();
        filter.ShouldContain("score:>=70.5");
        filter.ShouldContain("salaryMin:>=150000");
    }

    [Fact]
    public void A_posted_after_cutoff_becomes_a_posted_at_range_clause()
    {
        // The catalogue's `since:` narrows to jobs first posted at or after an absolute unix-second cutoff
        // (the relative "30d" is resolved to an instant before the query reaches the index).
        var filter = SearchFilterBuilder.Build(new SearchQuery { PostedAfter = 1_700_000_000 });

        filter.ShouldNotBeNull();
        filter.ShouldContain("postedAt:>=1700000000");
    }

    [Fact]
    public void Blank_terms_in_a_set_are_dropped_and_an_all_blank_set_adds_no_clause()
    {
        var filter = SearchFilterBuilder.Build(new SearchQuery
        {
            Technologies = ["", "   ", "C#"],
            Seniorities = ["", "  "],
        });

        filter.ShouldNotBeNull();
        filter.ShouldContain("technologies:=[`C#`]");
        filter.ShouldNotContain("seniority");
    }

    [Fact]
    public void All_typed_sets_are_mapped_to_their_fields()
    {
        var filter = SearchFilterBuilder.Build(new SearchQuery
        {
            Technologies = ["C#"],
            CompanyStages = ["SeriesB"],
            RemotePolicies = ["Remote"],
            Countries = ["NL"],
            Seniorities = ["Staff"],
        });

        filter.ShouldNotBeNull();
        filter.ShouldContain("technologies:=[`C#`]");
        filter.ShouldContain("companyStage:=[`SeriesB`]");
        filter.ShouldContain("remotePolicy:=[`Remote`]");
        filter.ShouldContain("countries:=[`NL`]");
        filter.ShouldContain("seniority:=[`Staff`]");
    }
}
