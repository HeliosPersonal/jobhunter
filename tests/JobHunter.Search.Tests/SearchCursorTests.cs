using JobHunter.Search;
using Shouldly;
using Xunit;

namespace JobHunter.Search.Tests;

/// <summary>
/// The keyset cursor (F9-T03). It is opaque and round-trips a <c>(score, id)</c> position; a cursor that
/// does not decode — mangled, truncated, or from a previous schema — is rejected rather than silently
/// yielding a wrong page (test-plan §edge cases). <see cref="SearchCursor"/> is internal, reached through
/// <c>InternalsVisibleTo</c>.
/// </summary>
public sealed class SearchCursorTests
{
    [Fact]
    public void A_position_round_trips_through_encode_and_decode()
    {
        var cursor = SearchCursor.Encode(87.25, "0192e8b7-0000-7000-8000-000000000001");

        SearchCursor.TryDecode(cursor, out var position).ShouldBeTrue();
        position.Score.ShouldBe(87.25);
        position.Id.ShouldBe("0192e8b7-0000-7000-8000-000000000001");
    }

    [Fact]
    public void The_cursor_is_opaque_and_carries_neither_the_score_nor_the_id_in_the_clear()
    {
        var cursor = SearchCursor.Encode(87.25, "the-job-id");

        cursor.ShouldNotContain("the-job-id");
        cursor.ShouldNotContain("87.25");
    }

    [Theory]
    [InlineData("not-base64!!")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_malformed_cursor_is_rejected_rather_than_decoded(string cursor)
    {
        SearchCursor.TryDecode(cursor, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_cursor_that_decodes_to_the_wrong_shape_is_rejected()
    {
        // Valid base64 of JSON that is not a cursor position — e.g. a cursor from a previous schema.
        var stale = Convert.ToBase64String("[1,2,3]"u8.ToArray());

        SearchCursor.TryDecode(stale, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_cursor_with_an_empty_id_is_rejected()
    {
        var empty = Convert.ToBase64String("{\"s\":10.0,\"i\":\"\"}"u8.ToArray());

        SearchCursor.TryDecode(empty, out _).ShouldBeFalse();
    }
}
