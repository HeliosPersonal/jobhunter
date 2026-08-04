namespace JobHunter.Application.Ranking;

/// <summary>
/// The two facts the ranking arithmetic needs from a <see cref="Domain.Intelligence.Match"/> (T07,
/// ADR-F4-0001): which job it is, and the model's 0–100 fit judgement. Deliberately not the whole
/// aggregate — <see cref="ScoreCalculator"/> is a pure function of explicit values, so it takes only the
/// numbers it uses and no CV text, no reasons, nothing that could leak or that would couple ordering to
/// the match's shape.
/// </summary>
/// <param name="JobId">The job being scored; the tie-break key and the score's identity.</param>
/// <param name="MatchScore">The model's fit judgement in <c>[0,100]</c>.</param>
public readonly record struct MatchFacts(Guid JobId, int MatchScore);
