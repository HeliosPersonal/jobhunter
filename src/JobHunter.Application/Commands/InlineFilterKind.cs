namespace JobHunter.Application.Commands;

/// <summary>
/// How an inline <c>key:value</c> filter's value is validated (catalogue §Argument parsing). The kind is
/// what turns <c>min:abc</c> into a named, forgiving error rather than a silent mis-filter: a value that
/// does not fit its kind is reported with the usage line, never concatenated into a query.
/// </summary>
public enum InlineFilterKind
{
    /// <summary>Any non-empty term, e.g. <c>tech:kafka</c>, <c>country:de</c>.</summary>
    Text = 1,

    /// <summary>A number, e.g. <c>min:70</c>; a non-numeric value is malformed.</summary>
    Number = 2,

    /// <summary>A duration like <c>30d</c> (digits then d/w/m/y); anything else is malformed.</summary>
    Duration = 3,

    /// <summary>A yes/no flag, e.g. <c>closed:yes</c>; any other value is malformed.</summary>
    Boolean = 4,
}
