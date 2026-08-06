namespace JobHunter.Domain.Applications;

/// <summary>
/// The outcome of asking <see cref="TransitionRules.Evaluate"/> whether one status may move to another —
/// a value, not an exception, because a refused transition is an expected business outcome
/// (coding-standards §4). A refusal always names a <see cref="Remedy"/>: what to do instead, because a
/// refusal without a remedy is just an obstacle ([[adr/0001-permissive-transitions-with-history|ADR-F6-0001]]).
/// A permission carries no remedy.
/// </summary>
public readonly record struct TransitionResult
{
    private TransitionResult(bool isPermitted, string? remedy)
    {
        IsPermitted = isPermitted;
        Remedy = remedy;
    }

    public bool IsPermitted { get; }

    /// <summary>The remedy message on a refusal; <c>null</c> when the transition is permitted.</summary>
    public string? Remedy { get; }

    public static TransitionResult Permitted() => new(true, remedy: null);

    public static TransitionResult Refused(string remedy)
    {
        if (string.IsNullOrWhiteSpace(remedy))
        {
            throw new ArgumentException("A refused transition must name a remedy.", nameof(remedy));
        }

        return new TransitionResult(false, remedy);
    }
}
