using JobHunter.Domain.Commands;

namespace JobHunter.Application.Commands;

/// <summary>
/// Matches a mistyped command token to the nearest registry name (AC-09, ADR-F10-0002, contract §Unknown
/// commands). It uses Damerau–Levenshtein distance — insertions, deletions, substitutions and adjacent
/// transpositions each cost one — so a single slip ("/pipline", "/serach") resolves to its command. Within
/// distance two it returns the nearest descriptor; beyond that it returns null, so the caller falls back to
/// the grouped list rather than guess. On a tie the earlier catalogue entry wins, keeping the answer stable
/// and order-defined. It is deterministic, instant and free — never an LLM.
/// </summary>
public static class CommandSuggester
{
    // Beyond two edits a "suggestion" is a guess; the contract draws the line here and falls back to the list.
    private const int MaxDistance = 2;

    /// <summary>The nearest command within <see cref="MaxDistance"/> edits of <paramref name="token"/>, or null.</summary>
    public static CommandDescriptor? Nearest(IReadOnlyList<CommandDescriptor> commands, string token)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(token);

        var needle = token.TrimStart('/').Trim();
        if (needle.Length == 0)
        {
            return null;
        }

        CommandDescriptor? best = null;
        var bestDistance = int.MaxValue;
        foreach (var command in commands)
        {
            var distance = DamerauLevenshtein(needle, command.Name);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = command;
            }
        }

        return bestDistance <= MaxDistance ? best : null;
    }

    // Optimal string alignment distance (adjacent transpositions cost one, no substring edited twice), which
    // is what "single typo" means for command names and what the misspelling corpus asserts.
    private static int DamerauLevenshtein(string source, string target)
    {
        var n = source.Length;
        var m = target.Length;
        var distance = new int[n + 1, m + 1];

        for (var i = 0; i <= n; i++)
        {
            distance[i, 0] = i;
        }

        for (var j = 0; j <= m; j++)
        {
            distance[0, j] = j;
        }

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = char.ToLowerInvariant(source[i - 1]) == char.ToLowerInvariant(target[j - 1]) ? 0 : 1;

                distance[i, j] = Math.Min(
                    Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                    distance[i - 1, j - 1] + cost);

                if (i > 1 && j > 1
                    && char.ToLowerInvariant(source[i - 1]) == char.ToLowerInvariant(target[j - 2])
                    && char.ToLowerInvariant(source[i - 2]) == char.ToLowerInvariant(target[j - 1]))
                {
                    distance[i, j] = Math.Min(distance[i, j], distance[i - 2, j - 2] + 1);
                }
            }
        }

        return distance[n, m];
    }
}
