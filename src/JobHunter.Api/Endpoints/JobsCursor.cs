using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobHunter.Api.Endpoints;

/// <summary>
/// The keyset cursor for the recent-jobs list (<c>GET /api/jobs</c>), a position on
/// <c>(firstSeenAt, id)</c> — the list's descending sort order. Like the search cursor it is a base64 of
/// a tiny JSON object so a client treats it as opaque and cannot forge a page into an offset scan, and a
/// cursor that does not decode — truncated, from a previous schema, or hand-made — is rejected as a clear
/// failure rather than silently yielding a wrong page. A cursor past the end simply matches nothing and
/// returns an empty page.
/// </summary>
internal static class JobsCursor
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>The keyset position of the last job on a page: its first-seen instant and its id.</summary>
    public readonly record struct Position(
        [property: JsonPropertyName("t")] long FirstSeenAt,
        [property: JsonPropertyName("i")] string Id);

    /// <summary>Encodes the last job's position into the opaque cursor a client sends back for the next page.</summary>
    public static string Encode(long firstSeenAt, Guid id)
    {
        var json = JsonSerializer.Serialize(new Position(firstSeenAt, id.ToString()), Options);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Decodes an opaque cursor. Returns false — never throws — for anything that is not a well-formed
    /// cursor whose id is a valid GUID, so the caller reports a clear "invalid cursor" failure instead of
    /// a wrong page.
    /// </summary>
    public static bool TryDecode(string cursor, out long firstSeenAt, out Guid id)
    {
        firstSeenAt = 0;
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var decoded = JsonSerializer.Deserialize<Position>(json, Options);
            if (!Guid.TryParse(decoded.Id, out var parsed))
            {
                return false;
            }

            firstSeenAt = decoded.FirstSeenAt;
            id = parsed;
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
