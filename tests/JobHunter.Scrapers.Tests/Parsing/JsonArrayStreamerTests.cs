using System.Text;
using JobHunter.Scrapers.Parsing;
using Shouldly;
using Xunit;

namespace JobHunter.Scrapers.Tests.Parsing;

/// <summary>
/// The element-range walker that lets a 400-posting board stream. It handles a named array property
/// (Greenhouse/Ashby/Workable), a root array (Lever), an absent array, and a truncated tail — the last of
/// which returns the intact leading elements rather than throwing (QG-1).
/// </summary>
public sealed class JsonArrayStreamerTests
{
    private static IReadOnlyList<Range> Ranges(string json, string? property) =>
        JsonArrayStreamer.ElementRanges(Encoding.UTF8.GetBytes(json), property);

    [Fact]
    public void NamedProperty_findsEachElement()
    {
        Ranges("{\"jobs\":[{\"id\":1},{\"id\":2}]}", "jobs").Count.ShouldBe(2);
    }

    [Fact]
    public void RootArray_isWalkedWhenPropertyIsNull()
    {
        Ranges("[{\"id\":1},{\"id\":2},{\"id\":3}]", null).Count.ShouldBe(3);
    }

    [Fact]
    public void AbsentNamedArray_yieldsNothing()
    {
        Ranges("{\"meta\":{\"total\":0}}", "jobs").ShouldBeEmpty();
    }

    [Fact]
    public void AbsentRootArray_yieldsNothing()
    {
        Ranges("{\"not\":\"an array\"}", null).ShouldBeEmpty();
    }

    [Fact]
    public void EmptyArray_yieldsNothing()
    {
        Ranges("{\"jobs\":[]}", "jobs").ShouldBeEmpty();
    }

    [Fact]
    public void TruncatedTail_keepsTheIntactLeadingElements()
    {
        Ranges("{\"jobs\":[{\"id\":1},{\"id\":2},{\"id\":", "jobs").Count.ShouldBe(2);
    }

    [Fact]
    public void ElementRange_slicesToTheExactObject()
    {
        var json = "{\"jobs\":[{\"id\":1},{\"id\":2}]}";
        var bytes = Encoding.UTF8.GetBytes(json);

        var ranges = JsonArrayStreamer.ElementRanges(bytes, "jobs");

        Encoding.UTF8.GetString(bytes.AsSpan(ranges[0])).ShouldBe("{\"id\":1}");
    }

    [Fact]
    public void PropertyNamedLikeTheArrayButNested_isNotMistaken()
    {
        // A nested "jobs" at depth > 1 must not be picked up; only the top-level array counts.
        Ranges("{\"meta\":{\"jobs\":[{\"id\":9}]},\"jobs\":[{\"id\":1}]}", "jobs").Count.ShouldBe(1);
    }
}
