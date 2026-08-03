using System.Collections.Frozen;
using JobHunter.Domain.Search;

namespace JobHunter.Search;

/// <summary>
/// One Typesense field definition (data-model §Typesense collection). Mirrors the wire shape Typesense's
/// collection API expects, so it serialises directly.
/// </summary>
/// <param name="Name">The field name — must be one of <see cref="JobDocument.FieldNames"/>.</param>
/// <param name="Type">The Typesense type: <c>string</c>, <c>string[]</c>, <c>int32</c>, <c>int64</c> or <c>float</c>.</param>
/// <param name="Facet">Whether the field is facetable (drives the refinement counts, AC-02).</param>
/// <param name="Sort">Whether the field is sortable.</param>
/// <param name="Optional">Whether a document may omit the field (the F3/F4/F6 nullable projections).</param>
public sealed record SearchField(string Name, string Type, bool Facet, bool Sort, bool Optional);

/// <summary>
/// The Typesense collection schema for <c>{env}_jobhunter_jobs</c> (data-model §Typesense collection),
/// built <em>from</em> <see cref="JobDocument.FieldNames"/> so the schema and the allowlist can never
/// drift: a field added to the document without a matching schema entry (or vice versa) fails
/// <see cref="EnsureCoversDocument"/>, which the index-scan suite asserts. The <c>token_separators</c> are
/// the detail that makes <c>C#</c>, <c>.NET</c> and <c>CI/CD</c> searchable as intended (data-model).
/// </summary>
public static class SearchSchema
{
    /// <summary>The default sorting field when a query gives no explicit sort (data-model).</summary>
    public const string DefaultSortingField = "score";

    /// <summary>
    /// <c>-</c>, <c>/</c>, <c>.</c> and <c>#</c>: with these as separators a search for <c>C#</c> finds
    /// <c>C#</c> and <c>node.js</c> finds <c>node</c> and <c>js</c> (data-model §token_separators).
    /// </summary>
    public static readonly IReadOnlyList<string> TokenSeparators = ["-", "/", ".", "#"];

    /// <summary>
    /// The field definitions, in the document's declared order. This is the authoritative schema: types,
    /// facets, sort and optionality all live here and nowhere else.
    /// </summary>
    public static readonly IReadOnlyList<SearchField> Fields =
    [
        new("id", "string", Facet: false, Sort: false, Optional: false),
        new("title", "string", Facet: false, Sort: true, Optional: false),
        new("companyName", "string", Facet: true, Sort: false, Optional: false),
        new("companyDomain", "string", Facet: false, Sort: false, Optional: false),
        new("description", "string", Facet: false, Sort: false, Optional: false),
        new("technologies", "string[]", Facet: true, Sort: false, Optional: false),
        new("countries", "string[]", Facet: true, Sort: false, Optional: false),
        new("remotePolicy", "string", Facet: true, Sort: false, Optional: false),
        new("seniority", "string", Facet: true, Sort: false, Optional: true),
        new("employmentType", "string", Facet: true, Sort: false, Optional: false),
        new("companyStage", "string", Facet: true, Sort: false, Optional: true),
        new("aiUsage", "string", Facet: true, Sort: false, Optional: true),
        new("salaryMin", "int32", Facet: true, Sort: false, Optional: true),
        new("salaryMax", "int32", Facet: false, Sort: false, Optional: true),
        new("salaryCurrency", "string", Facet: false, Sort: false, Optional: true),
        new("score", "float", Facet: false, Sort: true, Optional: false),
        new("postedAt", "int64", Facet: false, Sort: true, Optional: true),
        new("firstSeenAt", "int64", Facet: false, Sort: true, Optional: false),
        new("status", "string", Facet: true, Sort: false, Optional: false),
        new("applicationStatus", "string", Facet: true, Sort: false, Optional: true),
    ];

    /// <summary>The facetable field names, used to request facet counts on every search (AC-02).</summary>
    public static readonly IReadOnlyList<string> FacetFields =
        [.. Fields.Where(f => f.Facet).Select(f => f.Name)];

    private static readonly FrozenSet<string> SchemaFieldNames =
        Fields.Select(f => f.Name).ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Asserts the schema's field set is exactly <see cref="JobDocument.FieldNames"/> — no schema field
    /// the document lacks, no document field the schema lacks. The structural half of QG-2: a widening of
    /// the projection that forgot the schema, or a schema field with no backing allowlist entry, is a
    /// build failure. Returns false with the divergent names rather than throwing, so the test can report.
    /// </summary>
    public static bool EnsureCoversDocument(out IReadOnlyList<string> divergent)
    {
        var missingFromSchema = JobDocument.FieldNameSet.Where(n => !SchemaFieldNames.Contains(n));
        var missingFromDocument = SchemaFieldNames.Where(n => !JobDocument.FieldNameSet.Contains(n));
        divergent = [.. missingFromSchema, .. missingFromDocument];
        return divergent.Count == 0;
    }
}
