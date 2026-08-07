namespace JobHunter.Application.Commands;

/// <summary>
/// The set of inline <c>key:value</c> filters a command understands (catalogue §Argument parsing). The
/// dispatcher passes a command's vocabulary to the <see cref="ArgumentParser"/>; a token whose key is not
/// here is treated as free text, with a note, never rejected — the parser stays total (AC-09).
/// </summary>
public sealed class InlineFilterVocabulary
{
    /// <summary>A command with no inline filters — every <c>key:value</c> token is plain text.</summary>
    public static readonly InlineFilterVocabulary None = new([]);

    private readonly IReadOnlyDictionary<string, InlineFilterSpec> _byKey;

    public InlineFilterVocabulary(IReadOnlyList<InlineFilterSpec> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        var byKey = new Dictionary<string, InlineFilterSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var filter in filters)
        {
            if (!byKey.TryAdd(filter.Key, filter))
            {
                throw new InvalidOperationException($"The filter key '{filter.Key}' is declared more than once.");
            }
        }

        _byKey = byKey;
    }

    /// <summary>The spec for <paramref name="key"/> (case-insensitive), or null if the key is not a filter.</summary>
    public InlineFilterSpec? Find(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _byKey.GetValueOrDefault(key);
    }
}
