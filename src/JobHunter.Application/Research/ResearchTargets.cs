namespace JobHunter.Application.Research;

/// <summary>
/// The companies a research cycle will act on (SAD §6.1): the <see cref="Automatic"/> targets chosen from
/// the day's top jobs by score — at most five — and the <see cref="OnDemand"/> ones the Owner asked for.
/// The two lists are disjoint and each is free of duplicates: an on-demand request for a company already
/// picked automatically is dropped, so no company is researched twice in a cycle (AC-05).
/// </summary>
public sealed record ResearchTargets(
    IReadOnlyList<Guid> Automatic,
    IReadOnlyList<Guid> OnDemand);
