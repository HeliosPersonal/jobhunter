using System.Reflection;
using JobHunter.Domain.Search;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Search;

/// <summary>
/// The structural half of QG-2 (T01): <see cref="JobDocument"/> is a hand-written allowlist, and its
/// declared <see cref="JobDocument.FieldNames"/> is the single source of truth the Typesense schema is
/// built from. If a property is added without a matching entry in <c>FieldNames</c> (or vice versa), this
/// suite fails — which is how a future widening of the projection becomes a build failure rather than a
/// leak.
/// </summary>
public sealed class JobDocumentTests
{
    private static readonly HashSet<string> ForbiddenFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "reasons",
        "matchReasons",
        "missingSkills",
        "applicationNotes",
        "notes",
        "interviewProbability",
        "preferenceWeights",
        "cv",
        "cvContent",
    };

    [Fact]
    public void FieldNames_exactly_equals_the_records_properties_camelCased()
    {
        var propertyNames = typeof(JobDocument)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Select(p => CamelCase(p.Name))
            .ToHashSet(StringComparer.Ordinal);

        JobDocument.FieldNameSet.ShouldBe(propertyNames, ignoreOrder: true);
    }

    [Fact]
    public void FieldNames_has_no_duplicates()
    {
        JobDocument.FieldNames.Count.ShouldBe(JobDocument.FieldNameSet.Count);
    }

    [Fact]
    public void No_field_carries_a_cv_or_match_related_name()
    {
        foreach (var field in JobDocument.FieldNames)
        {
            ForbiddenFieldNames.ShouldNotContain(field);
        }
    }

    private static string CamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
