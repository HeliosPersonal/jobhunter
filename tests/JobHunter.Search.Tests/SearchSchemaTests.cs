using System.Text.Json;
using JobHunter.Domain.Search;
using JobHunter.Search;
using Shouldly;
using Xunit;

namespace JobHunter.Search.Tests;

/// <summary>
/// The schema is built from <see cref="JobDocument.FieldNames"/> and can never drift from it — the
/// structural half of QG-2 at the Typesense boundary (F9-T02). These assertions also pin the two details
/// the data-model calls out by name: <c>default_sorting_field</c> is <c>score</c>, and the
/// <c>token_separators</c> make <c>C#</c>, <c>.NET</c> and <c>CI/CD</c> searchable.
/// </summary>
public sealed class SearchSchemaTests
{
    [Fact]
    public void The_schema_field_set_is_exactly_the_document_allowlist()
    {
        var covers = SearchSchema.EnsureCoversDocument(out var divergent);

        covers.ShouldBeTrue($"schema and document diverge on: {string.Join(", ", divergent)}");
        divergent.ShouldBeEmpty();
    }

    [Fact]
    public void The_serialised_schema_declares_score_as_the_default_sort()
    {
        using var doc = JsonDocument.Parse(TypesenseSerialization.SchemaJson("test_jobhunter_jobs"));

        doc.RootElement.GetProperty("default_sorting_field").GetString().ShouldBe("score");
        doc.RootElement.GetProperty("name").GetString().ShouldBe("test_jobhunter_jobs");
    }

    [Fact]
    public void The_token_separators_make_hash_slash_dot_and_dash_searchable()
    {
        using var doc = JsonDocument.Parse(TypesenseSerialization.SchemaJson("test_jobhunter_jobs"));

        var separators = doc.RootElement.GetProperty("token_separators")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        // With '#' a search for "C#" tokenises to "c" + "#"; with '.' "node.js" splits; with '/' "CI/CD".
        separators.ShouldBe(["-", "/", ".", "#"]);
    }

    [Fact]
    public void Every_faceted_field_is_declared_facetable_in_the_serialised_schema()
    {
        using var doc = JsonDocument.Parse(TypesenseSerialization.SchemaJson("test_jobhunter_jobs"));
        var fields = doc.RootElement.GetProperty("fields").EnumerateArray().ToList();

        foreach (var facet in SearchSchema.FacetFields)
        {
            var field = fields.Single(f => f.GetProperty("name").GetString() == facet);
            field.GetProperty("facet").GetBoolean().ShouldBeTrue();
        }
    }

    [Fact]
    public void An_optional_field_is_marked_optional_and_a_required_field_is_not()
    {
        using var doc = JsonDocument.Parse(TypesenseSerialization.SchemaJson("test_jobhunter_jobs"));
        var fields = doc.RootElement.GetProperty("fields").EnumerateArray().ToList();

        var seniority = fields.Single(f => f.GetProperty("name").GetString() == "seniority");
        seniority.GetProperty("optional").GetBoolean().ShouldBeTrue();

        var title = fields.Single(f => f.GetProperty("name").GetString() == "title");
        title.TryGetProperty("optional", out _).ShouldBeFalse();
    }
}
