namespace JobHunter.Domain.Reporting;

/// <summary>
/// One card <em>below today's cut</em> (F10 T06 <c>/more</c>, catalogue §Digest and discovery): a role the
/// latest Run scored high enough to show (≥ the card threshold) and did not suppress, but which ranked
/// outside the digest's top cards. It pairs the job's <see cref="CardDisplayFacts"/> — the same display
/// primitives a digest card is rendered from — with the <see cref="Score"/> and <see cref="Reasons"/> the
/// ranking assembled it with (invariant 4), so <c>/more</c> renders it through the one shared card layout
/// rather than a second one.
///
/// <para>The ordering is read from the <em>frozen</em> stored scores, never recomputed, so paging through
/// <c>/more</c> mid-morning cannot reshuffle what the Owner already saw ([[PRD]] §8). It carries
/// <strong>nothing about the Owner's CV</strong> — the CV crosses exactly one boundary, and it is not this
/// one (F4 invariant).</para>
/// </summary>
/// <param name="Display">The job's display facts — title, company, stage, location and salary.</param>
/// <param name="Score">The role's final 0–100 score, shown on the card.</param>
/// <param name="Reasons">The ranking's own explanation for this role — never empty (invariant 4).</param>
public sealed record MoreCard(
    CardDisplayFacts Display,
    decimal Score,
    IReadOnlyList<string> Reasons);
