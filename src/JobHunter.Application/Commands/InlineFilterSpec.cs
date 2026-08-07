namespace JobHunter.Application.Commands;

/// <summary>
/// One inline filter a command understands — its key (the token before the colon) and the
/// <see cref="InlineFilterKind"/> that validates its value (catalogue §Argument parsing). A command with
/// no filters passes <see cref="InlineFilterVocabulary.None"/>, so every <c>key:value</c> token is plain
/// text; that keeps the parser total (a colon in an ordinary term never becomes an error).
/// </summary>
public sealed record InlineFilterSpec
{
    public InlineFilterSpec(string key, InlineFilterKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException($"'{kind}' is not a valid filter kind.", nameof(kind));
        }

        Key = key.ToLowerInvariant();
        Kind = kind;
    }

    /// <summary>The filter key, lower-cased, e.g. <c>tech</c>; matched case-insensitively against input.</summary>
    public string Key { get; }

    /// <summary>How the value is validated.</summary>
    public InlineFilterKind Kind { get; }
}
