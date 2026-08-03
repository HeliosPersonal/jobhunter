using JobHunter.Api.Endpoints;
using Shouldly;
using Xunit;

namespace JobHunter.Api.Tests;

/// <summary>
/// The recent-jobs list cursor is an opaque, round-trippable keyset position on <c>(firstSeenAt, id)</c>
/// (T05). It survives an encode/decode round trip, and rejects — never throws on — a mangled cursor, a
/// cursor from a previous schema, or one whose id is not a GUID.
/// </summary>
public sealed class JobsCursorTests
{
    [Fact]
    public void A_position_survives_an_encode_decode_round_trip()
    {
        var id = Guid.NewGuid();
        var encoded = JobsCursor.Encode(1_722_600_000, id);

        JobsCursor.TryDecode(encoded, out var firstSeen, out var decodedId).ShouldBeTrue();
        firstSeen.ShouldBe(1_722_600_000);
        decodedId.ShouldBe(id);
    }

    [Fact]
    public void The_cursor_is_opaque_base64_not_a_readable_offset()
    {
        var encoded = JobsCursor.Encode(1, Guid.NewGuid());

        // A client cannot read a page number or offset out of it.
        encoded.ShouldNotContain("firstSeenAt");
        Convert.TryFromBase64String(encoded, new byte[encoded.Length], out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!not-base64!!!")]
    public void A_blank_or_malformed_cursor_is_rejected_without_throwing(string cursor)
    {
        JobsCursor.TryDecode(cursor, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_cursor_from_a_previous_schema_shape_is_rejected()
    {
        // Valid base64 of a JSON object that is not this cursor's shape (no GUID id).
        var previous = Convert.ToBase64String("{\"page\":3,\"size\":20}"u8.ToArray());

        JobsCursor.TryDecode(previous, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_cursor_whose_id_is_not_a_guid_is_rejected()
    {
        var bad = Convert.ToBase64String("{\"t\":10,\"i\":\"not-a-guid\"}"u8.ToArray());

        JobsCursor.TryDecode(bad, out _, out _).ShouldBeFalse();
    }
}
