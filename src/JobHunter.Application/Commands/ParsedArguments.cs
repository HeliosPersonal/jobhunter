using JobHunter.Domain.Commands;

namespace JobHunter.Application.Commands;

/// <summary>One recognised inline filter, lifted out of the raw line into a typed key/value pair.</summary>
public sealed record ParsedFilter(string Key, string Value);

/// <summary>
/// The structured result of <see cref="ArgumentParser.Parse"/> — the whole point of T02: user input
/// arrives as typed values (<see cref="FreeText"/> plus a list of <see cref="Filters"/>), never as one
/// blob a query builder would concatenate into a filter expression (F9 SAD §8, done-when #6).
///
/// <para>Factory methods make each outcome unambiguous and keep an invalid combination (e.g. a
/// <see cref="ParseStatus.Malformed"/> result with no problem text) unconstructable.</para>
/// </summary>
public sealed record ParsedArguments
{
    private ParsedArguments(
        ParseStatus status,
        string freeText,
        IReadOnlyList<ParsedFilter> filters,
        IReadOnlyList<string> notes,
        ArgumentSpec? missingArgument,
        string? problem,
        string? usage)
    {
        Status = status;
        FreeText = freeText;
        Filters = filters;
        Notes = notes;
        MissingArgument = missingArgument;
        Problem = problem;
        Usage = usage;
    }

    /// <summary>The parse outcome.</summary>
    public ParseStatus Status { get; }

    /// <summary>The free-text terms with all recognised filters removed; never carries filter syntax.</summary>
    public string FreeText { get; }

    /// <summary>The recognised inline filters as typed pairs, in order, deduplicated.</summary>
    public IReadOnlyList<ParsedFilter> Filters { get; }

    /// <summary>Non-fatal remarks for the reply — an unknown filter treated as text, extra args ignored.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>The argument to ask for when <see cref="Status"/> is <see cref="ParseStatus.NeedsInput"/>.</summary>
    public ArgumentSpec? MissingArgument { get; }

    /// <summary>What was wrong, for a <see cref="ParseStatus.Malformed"/> result; null otherwise.</summary>
    public string? Problem { get; }

    /// <summary>The command's usage line, shown alongside a malformed-value reply; null otherwise.</summary>
    public string? Usage { get; }

    internal static ParsedArguments Complete(
        string freeText, IReadOnlyList<ParsedFilter> filters, IReadOnlyList<string> notes) =>
        new(ParseStatus.Complete, freeText, filters, notes, missingArgument: null, problem: null, usage: null);

    internal static ParsedArguments NeedsInput(ArgumentSpec missingArgument) =>
        new(ParseStatus.NeedsInput, string.Empty, [], [], missingArgument, problem: null, usage: null);

    internal static ParsedArguments Malformed(string problem, string usage) =>
        new(ParseStatus.Malformed, string.Empty, [], [], missingArgument: null, problem, usage);
}
