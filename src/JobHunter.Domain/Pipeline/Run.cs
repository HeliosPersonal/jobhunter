using JobHunter.Domain.Common;

namespace JobHunter.Domain.Pipeline;

/// <summary>
/// One day's intelligence work as a durable, resumable, cost-bounded state machine (ADR-F3-0001,
/// data-model §runs). The Run does not live in a call stack — a five-hour process cannot — so its
/// <see cref="State"/> is an explicit column and every step is a load-act-commit against it. On startup
/// the orchestrator loads every non-terminal Run and re-enters it; that single behaviour is the whole of
/// QG-1.
///
/// <para>The transition table (<see cref="RunTransitions"/>) is data, so an illegal transition is one
/// rejection returned as an <see cref="Error"/>, never an emergent property of scattered conditionals.
/// <see cref="CeilingUsd"/> is captured at construction and immutable, so changing configuration mid-Run
/// cannot retroactively authorise spend (ADR-F3-0002). The aggregate depends on nothing external — no EF
/// Core, no Anthropic, no Wolverine — which is why the state machine is unit-testable without any of the
/// infrastructure it coordinates.</para>
/// </summary>
public sealed class Run : Entity
{
    public static readonly Error IllegalTransition =
        new("run.transition.illegal", "The attempted Run state transition is not permitted.");

    public static readonly Error AlreadyTerminal =
        new("run.transition.terminal", "A terminal Run cannot transition further.");

    public static readonly Error CutoffOutOfOrder =
        new("run.cutoff.out_of_order", "A Run's cutoff_from must not be after its cutoff_to.");

    public Run(
        Guid id,
        DateTimeOffset cutoffFrom,
        DateTimeOffset cutoffTo,
        decimal ceilingUsd,
        DateTimeOffset startedAt)
        : base(id)
    {
        if (cutoffFrom > cutoffTo)
        {
            // A programmer error, not a business outcome: a caller building a Run with an inverted window.
            throw new ArgumentException("A Run's cutoff_from must not be after its cutoff_to.", nameof(cutoffFrom));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ceilingUsd);

        CutoffFrom = cutoffFrom;
        CutoffTo = cutoffTo;
        CeilingUsd = ceilingUsd;
        StartedAt = startedAt;
        State = RunState.Created;
    }

    private Run()
    {
    }

    public RunState State { get; private set; }

    /// <summary>The start of the discovery window — the previous Run's <see cref="CutoffTo"/>.</summary>
    public DateTimeOffset CutoffFrom { get; private set; }

    /// <summary>The end of the discovery window.</summary>
    public DateTimeOffset CutoffTo { get; private set; }

    /// <summary>Snapshotted at creation; never mutated (ADR-F3-0002).</summary>
    public decimal CeilingUsd { get; private init; }

    /// <summary>Denormalised sum of the ledger; the ledger is authoritative (data-model §runs).</summary>
    public decimal SpentUsd { get; private set; }

    public int JobsInScope { get; private set; }

    /// <summary>Items whose batch did not complete before the deadline (AC-09).</summary>
    public int JobsCarriedOver { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>Plain language; surfaced in the digest footer.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>Records the number of jobs the enrichment stage will assess. Idempotent — safe on resume.</summary>
    public void SetScope(int jobsInScope)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(jobsInScope);
        JobsInScope = jobsInScope;
    }

    /// <summary>Records how many items were carried over to the next Run (AC-09). Never negative.</summary>
    public void RecordCarryOver(int jobsCarriedOver)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(jobsCarriedOver);
        JobsCarriedOver = jobsCarriedOver;
    }

    /// <summary>
    /// Sets the denormalised spend to the ledger's authoritative total. Called after each ledger write so
    /// "what did last night cost" is a single-row read (data-model §runs).
    /// </summary>
    public void SetSpend(decimal spentUsd)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(spentUsd);
        SpentUsd = spentUsd;
    }

    /// <summary>
    /// Attempts the transition to <paramref name="to"/> at <paramref name="at"/>. Returns a failure — never
    /// throws — when the pair is not a legal edge or the Run is already terminal (T01). Stamps
    /// <see cref="FinishedAt"/> on reaching any terminal state. Idempotent for terminal re-entry only via
    /// <see cref="Abort"/>; an ordinary transition to the same state is illegal unless the table lists it.
    /// </summary>
    public Result<Run> TransitionTo(RunState to, DateTimeOffset at)
    {
        if (RunTransitions.IsTerminal(State))
        {
            return AlreadyTerminal;
        }

        if (!RunTransitions.IsLegal(State, to))
        {
            return new Error(
                IllegalTransition.Code,
                $"Illegal Run transition {State} -> {to}.");
        }

        State = to;
        if (RunTransitions.IsTerminal(to))
        {
            FinishedAt = at;
        }

        return Result<Run>.Success(this);
    }

    /// <summary>
    /// Ends the Run in a terminal state from any non-terminal state, recording <paramref name="reason"/>.
    /// A cost breach aborts to <see cref="RunState.CostAborted"/>; any other unrecoverable fault to
    /// <see cref="RunState.Failed"/>. Idempotent: aborting an already-terminal Run keeps its first reason
    /// and finish instant, so a crash-and-resume that re-aborts converges rather than overwriting (QG-1).
    /// </summary>
    public Run Abort(string reason, DateTimeOffset at, bool costBreach)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (RunTransitions.IsTerminal(State))
        {
            return this;
        }

        State = costBreach ? RunState.CostAborted : RunState.Failed;
        FailureReason = reason;
        FinishedAt = at;
        return this;
    }
}
