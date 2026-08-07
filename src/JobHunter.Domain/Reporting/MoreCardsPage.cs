namespace JobHunter.Domain.Reporting;

/// <summary>
/// One page of the roles below today's cut (F10 T06 <c>/more</c>): the cards to show now, and how many
/// there are in total below the cut so the reply can report "Next 5 of 23 below the cut." The total is the
/// whole below-the-cut set, not the page — it is what tells the Owner whether another <c>/more</c> is worth
/// asking for.
/// </summary>
/// <param name="Cards">The cards to render now, best-score first, at most the caller's requested count.</param>
/// <param name="TotalBelowTheCut">How many roles sit below the cut in all, so the reply can report the remainder.</param>
public sealed record MoreCardsPage(
    IReadOnlyList<MoreCard> Cards,
    int TotalBelowTheCut);
