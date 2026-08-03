namespace JobHunter.Domain.Pipeline;

/// <summary>
/// The Run state machine as <em>data</em> (SAD §6.1), not scattered <c>if</c> statements — an illegal
/// transition is one rejection point rather than an emergent property (T01). The set below is the exact
/// edge list of the SAD §6.1 diagram. Terminal states (<see cref="RunState.Delivered"/>,
/// <see cref="RunState.Failed"/>, <see cref="RunState.CostAborted"/>) have no outgoing edges.
/// </summary>
public static class RunTransitions
{
    private static readonly HashSet<(RunState From, RunState To)> Legal =
        new()
        {
            (RunState.Created, RunState.Enriching),
            (RunState.Created, RunState.CostAborted),
            (RunState.Enriching, RunState.Matching),
            (RunState.Enriching, RunState.CostAborted),
            (RunState.Enriching, RunState.Failed),
            (RunState.Matching, RunState.Ranking),
            (RunState.Matching, RunState.Failed),
            (RunState.Ranking, RunState.Researching),
            (RunState.Researching, RunState.Reporting),
            (RunState.Reporting, RunState.Delivered),
            (RunState.Reporting, RunState.Failed),
        };

    /// <summary>The three terminal states — no outgoing transition is legal from any of them.</summary>
    public static readonly IReadOnlySet<RunState> Terminal =
        new HashSet<RunState> { RunState.Delivered, RunState.Failed, RunState.CostAborted };

    /// <summary>True when <paramref name="state"/> is terminal (no work left, no resumption).</summary>
    public static bool IsTerminal(RunState state) => Terminal.Contains(state);

    /// <summary>True when moving from <paramref name="from"/> to <paramref name="to"/> is a legal edge.</summary>
    public static bool IsLegal(RunState from, RunState to) => Legal.Contains((from, to));
}
