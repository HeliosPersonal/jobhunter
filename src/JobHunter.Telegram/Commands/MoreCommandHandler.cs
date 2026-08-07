using System.Globalization;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/more [count]</c> (contract §Digest and discovery): the next cards below today's cut, in rank order,
/// from the same stored digest. Re-ranking mid-morning would make the ordering unstable, so this
/// <em>paginates</em> the frozen below-the-cut set through <see cref="IMoreCardsQuery"/> rather than
/// recomputing ([[PRD]] §8): the latest Run's roles that scored high enough to show and were not suppressed,
/// but ranked outside the digest's top cards. Each is rendered through the one shared
/// <see cref="CardFormatter"/> (done-when 6) — there is no second layout — and the reply leads with how many
/// sit below the cut in all, so the Owner knows whether another <c>/more</c> is worth asking for.
///
/// <para>The optional <c>count</c> is clamped to the catalogue's 1–20 (default 5): a missing or non-numeric
/// argument falls back to the default rather than erroring, a value below one is raised to one and one above
/// the cap is lowered to it. Reading is all it does — no LLM, no write, and <strong>no CV</strong> (the CV
/// crosses exactly one boundary, and it is not this one). An empty below-the-cut set is answered with a
/// single plain line, so the Owner always gets a reply.</para>
/// </summary>
internal sealed class MoreCommandHandler(
    IMoreCardsQuery more, ILogger<MoreCommandHandler> logger) : ICommandHandler
{
    /// <summary>The count shown when none is given (catalogue §Digest and discovery: <c>count</c> default 5).</summary>
    private const int DefaultCount = 5;

    /// <summary>The catalogue's <c>count</c> bounds — 1–20 — a value outside is clamped, never rejected.</summary>
    private const int MinCount = 1;
    private const int MaxCount = 20;

    private readonly IMoreCardsQuery _more = more ?? throw new ArgumentNullException(nameof(more));
    private readonly ILogger<MoreCommandHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var count = ResolveCount(request.Arguments);
        var page = await _more.BelowTheCutAsync(count, cancellationToken).ConfigureAwait(false);

        if (page.Cards.Count == 0)
        {
            _logger.LogDebug("/more requested but nothing is below today's cut.");
            return [RenderedMessage.PlainText(
                "_" + MarkdownV2Escaper.Escape("Nothing more below the cut.") + "_")];
        }

        // Header first: the page size shown and the whole below-the-cut total, so the Owner can see how many
        // remain — "Next 5 of 23 below the cut." Then one card per role, in the frozen stored order.
        var messages = new List<RenderedMessage>
        {
            RenderedMessage.PlainText(MarkdownV2Escaper.Escape(
                $"Next {page.Cards.Count} of {page.TotalBelowTheCut} below the cut.")),
        };

        messages.AddRange(page.Cards.Select(card =>
            RenderedMessage.PlainText(CardFormatter.Format(ToView(card)))));

        return messages;
    }

    // The count argument, clamped to the catalogue's 1–20. A missing or non-numeric value is not an error —
    // it falls back to the default (argument-parsing table: a malformed value never errors the reply).
    private static int ResolveCount(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)
            || !int.TryParse(arguments.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var requested))
        {
            return DefaultCount;
        }

        return Math.Clamp(requested, MinCount, MaxCount);
    }

    // Maps the below-the-cut card onto the shared display view: the same salary/location composition the
    // digest renderer uses, so a below-the-cut card reads exactly like one above it.
    private static CardView ToView(MoreCard card) => new(
        card.Display.Title,
        card.Display.Company,
        card.Display.Stage,
        LocationSummary(card.Display),
        BuildSalary(card.Display),
        card.Score,
        card.Reasons);

    private static string LocationSummary(CardDisplayFacts display) =>
        display.Countries.Count > 0
            ? string.Join(", ", display.Countries)
            : display.RemotePolicy;

    private static CardSalary? BuildSalary(CardDisplayFacts display)
    {
        // A published range is stated plainly; only when the board published none does the card fall back to
        // the model's estimate, marked (est) with its confidence band and never presented as fact.
        if (display.PublishedSalaryMin is { } pubMin && display.PublishedSalaryMax is { } pubMax)
        {
            return new CardSalary(pubMin, pubMax, display.PublishedSalaryCurrency, IsEstimate: false, null);
        }

        if (display.EstimatedSalaryMin is { } estMin && display.EstimatedSalaryMax is { } estMax)
        {
            return new CardSalary(
                estMin, estMax, display.EstimatedSalaryCurrency, IsEstimate: true,
                ConfidenceBand(display.EstimatedSalaryConfidence));
        }

        return null;
    }

    private static string? ConfidenceBand(decimal? confidence) => confidence switch
    {
        null => null,
        >= 0.7m => "high conf",
        >= 0.4m => "med conf",
        _ => "low conf",
    };
}
