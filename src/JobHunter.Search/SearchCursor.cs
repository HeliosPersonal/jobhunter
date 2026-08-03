using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobHunter.Search;

/// <summary>
/// The cursor is a keyset position on <c>(score, id)</c> — the sort order every search uses (SAD §8,
/// "cursor-based on (score, id); no offset paging"). It is a base64 of a tiny JSON object
/// <c>{"s":&lt;score&gt;,"i":"&lt;id&gt;"}</c>, so a client treats it as opaque and cannot forge a page
/// into an offset scan. A cursor that does not decode — truncated, from a previous schema, or hand-made —
/// is rejected as a clear failure rather than silently yielding a wrong page (test-plan §edge cases).
/// </summary>
internal static class SearchCursor
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>The keyset position of the last hit on a page: its score and its id.</summary>
    public readonly record struct Position(
        [property: JsonPropertyName("s")] double Score,
        [property: JsonPropertyName("i")] string Id);

    /// <summary>Encodes the last hit's position into the opaque cursor a client sends back for the next page.</summary>
    public static string Encode(double score, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var json = JsonSerializer.Serialize(new Position(score, id), Options);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Decodes an opaque cursor. Returns false — never throws — for anything that is not a well-formed
    /// cursor, so the caller reports a clear "invalid cursor" failure instead of a wrong page.
    /// </summary>
    public static bool TryDecode(string cursor, out Position position)
    {
        position = default;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var decoded = JsonSerializer.Deserialize<Position>(json, Options);
            if (string.IsNullOrEmpty(decoded.Id))
            {
                return false;
            }

            position = decoded;
            return true;
        }
        catch (FormatException)
        {
            // Not valid base64 — a mangled or hand-made cursor.
            return false;
        }
        catch (JsonException)
        {
            // Decoded, but not the cursor shape — e.g. a cursor from a previous schema version.
            return false;
        }
    }
}
