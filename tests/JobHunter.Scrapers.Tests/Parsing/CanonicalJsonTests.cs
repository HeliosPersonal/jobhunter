using System.Text.Json;
using JobHunter.Scrapers.Parsing;
using Shouldly;
using Xunit;

namespace JobHunter.Scrapers.Tests.Parsing;

/// <summary>
/// The canonical form a posting is hashed over: key order is stabilised at every depth, volatile keys are
/// dropped, and named keys can be replaced by a derived value (HTML → plain text). Two payloads that
/// differ only in key order, in a volatile field, or in markup that strips to the same text hash the same.
/// </summary>
public sealed class CanonicalJsonTests
{
    private static readonly IReadOnlySet<string> NoVolatile = new HashSet<string>();
    private static readonly IReadOnlyDictionary<string, Func<JsonElement, string>> NoTransforms =
        new Dictionary<string, Func<JsonElement, string>>();

    private static string Canonical(
        string json,
        IReadOnlySet<string>? volatileKeys = null,
        IReadOnlyDictionary<string, Func<JsonElement, string>>? transforms = null)
    {
        using var document = JsonDocument.Parse(json);
        return CanonicalJson.Canonicalise(
            document.RootElement, volatileKeys ?? NoVolatile, transforms ?? NoTransforms);
    }

    [Fact]
    public void KeyOrder_isNormalised_atEveryDepth()
    {
        Canonical("{\"b\":1,\"a\":{\"y\":2,\"x\":3}}")
            .ShouldBe(Canonical("{\"a\":{\"x\":3,\"y\":2},\"b\":1}"));
    }

    [Fact]
    public void VolatileKeys_areDropped()
    {
        var withTouch = Canonical("{\"id\":1,\"updated_at\":\"A\"}", new HashSet<string> { "updated_at" });
        var without = Canonical("{\"id\":1,\"updated_at\":\"B\"}", new HashSet<string> { "updated_at" });

        withTouch.ShouldBe(without);
        withTouch.ShouldNotContain("updated_at");
    }

    [Fact]
    public void Transform_replacesTheValueWithDerivedText()
    {
        var transforms = new Dictionary<string, Func<JsonElement, string>>
        {
            ["content"] = e => e.GetString()!.ToUpperInvariant(),
        };

        Canonical("{\"content\":\"hi\"}", transforms: transforms)
            .ShouldBe("{\"content\":\"HI\"}");
    }

    [Fact]
    public void Arrays_arePreservedInOrder()
    {
        Canonical("{\"xs\":[3,1,2]}").ShouldBe("{\"xs\":[3,1,2]}");
    }

    [Fact]
    public void NestedArraysOfObjects_haveKeysSorted()
    {
        Canonical("{\"xs\":[{\"b\":1,\"a\":2}]}").ShouldBe("{\"xs\":[{\"a\":2,\"b\":1}]}");
    }

    [Fact]
    public void NonObjectRoot_isCanonicalisedDirectly()
    {
        Canonical("[{\"b\":1,\"a\":2}]").ShouldBe("[{\"a\":2,\"b\":1}]");
    }

    [Fact]
    public void ScalarValues_areEmittedVerbatim()
    {
        Canonical("{\"n\":42,\"b\":true,\"z\":null}").ShouldContain("42");
    }
}
