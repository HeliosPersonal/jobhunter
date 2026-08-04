namespace JobHunter.Application.Ranking;

/// <summary>
/// The output of <see cref="AlignmentCalculator.Calculate"/> (T14): the career-alignment component in
/// <c>[0,1]</c> and the reason that explains it. The reason is not persisted on the <c>scores</c> row —
/// it is the accountability trail invariant 4 requires at the point the number is computed, asserted by
/// the golden set and the calculator's own tests — so an alignment value never travels without an
/// explanation of the two signals it was blended from.
/// </summary>
/// <param name="Value">The alignment component in <c>[0,1]</c>; the <c>alignment</c> input to the formula.</param>
/// <param name="Reason">A human-readable explanation naming the role family and the AI-usage level.</param>
public readonly record struct AlignmentResult(decimal Value, string Reason);
