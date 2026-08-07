using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Callbacks;

namespace JobHunter.Telegram.Formatting;

/// <summary>
/// The production <see cref="IDigestRenderer"/> (F5 T12) both the 07:00 <c>DeliveryHandler</c> and
/// <c>/digest</c> depend on. It turns a persisted <see cref="Digest"/> into the ordered, keyed message
/// sequence the delivery loop sends — the header (<see cref="CardKey.Header"/>), one card per rank
/// (<see cref="DigestCard.Key"/>), then the footer (<see cref="CardKey.Footer"/>) when it has anything to
/// say. It joins each card's <em>display</em> facts fresh through <see cref="ICardDisplayQuery"/> — the
/// title, company, location and salary the <see cref="DigestCard"/> never snapshots — so a re-rendered
/// <c>/digest</c> shows the job as it stands, and it maps them onto the one shared
/// <see cref="CardView"/>/<see cref="CardFormatter"/> rather than a second layout.
///
/// <para>Every card carries the fixed four-button keyboard (contract §Card): Open as a URL button that
/// never reaches the bot, and Ignore/Save/Applied as callback buttons whose payload is the HMAC short id so
/// a tap resolves back to the card (T10). A card whose job facts have gone (deleted between assembly and
/// render) is skipped rather than shown as a fabricated blank. It reads only stored digest state and public
/// job facts — <strong>never the CV</strong> (the CV crosses exactly one boundary, and it is not this one).</para>
/// </summary>
internal sealed class DigestRenderer(ICardDisplayQuery facts, CallbackDataCodec codec) : IDigestRenderer
{
    // The contract's callback action tokens (open|ign|sav|app). Open is a URL button and carries no token.
    private const string IgnoreToken = "ign";
    private const string SaveToken = "sav";
    private const string AppliedToken = "app";

    private readonly ICardDisplayQuery _facts = facts ?? throw new ArgumentNullException(nameof(facts));
    private readonly CallbackDataCodec _codec = codec ?? throw new ArgumentNullException(nameof(codec));

    public async Task<IReadOnlyList<RenderableMessage>> RenderAsync(
        Digest digest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(digest);

        // One round-trip for the whole digest's display facts; a card whose job is gone is simply absent.
        var jobIds = digest.Cards.Select(c => c.JobId).ToArray();
        var displayFacts = await _facts.DisplayFactsAsync(jobIds, cancellationToken).ConfigureAwait(false);

        var messages = new List<RenderableMessage>();

        // Cards first, in rank order, skipping any whose facts have gone — the count feeds the header line.
        var cardMessages = new List<RenderableMessage>();
        foreach (var card in digest.Cards)
        {
            if (!displayFacts.TryGetValue(card.JobId, out var display))
            {
                continue;
            }

            cardMessages.Add(new RenderableMessage(card.Key, RenderCard(card, display)));
        }

        // Header, then the cards, then the footer when it has content.
        var top = ResolveTopOpportunity(digest, displayFacts);
        var headerText = DigestHeaderFormatter.Format(BuildHeader(digest, cardMessages.Count, top));
        messages.Add(new RenderableMessage(CardKey.Header, RenderedMessage.PlainText(headerText)));

        messages.AddRange(cardMessages);

        var footer = DigestFooterFormatter.Format(BuildFooter(digest));
        if (footer is not null)
        {
            messages.Add(new RenderableMessage(CardKey.Footer, RenderedMessage.PlainText(footer)));
        }

        return messages;
    }

    private RenderedMessage RenderCard(DigestCard card, CardDisplayFacts display)
    {
        var view = new CardView(
            display.Title,
            display.Company,
            display.Stage,
            LocationSummary(display),
            BuildSalary(display),
            card.Score,
            card.Reasons);

        var text = CardFormatter.Format(view);
        var shortId = _codec.Encode(card.Key);

        // The one keyboard the contract fixes: Open (URL, never reaches the bot), then the three signed
        // callback actions. Rewritten after a tap by the callback handler, not here.
        IReadOnlyList<IReadOnlyList<InlineButton>> keyboard =
        [
            [
                InlineButton.ForUrl("Open", display.ApplyUrl),
                new InlineButton("Ignore", $"{IgnoreToken}:{shortId}"),
                new InlineButton("Save", $"{SaveToken}:{shortId}"),
                new InlineButton("Applied", $"{AppliedToken}:{shortId}"),
            ],
        ];

        return new RenderedMessage(text, keyboard);
    }

    private static HeaderView BuildHeader(Digest digest, int cardCount, HeaderOpportunity? top)
    {
        // The top two suppression reasons, most-hidden first, summarised in the header's hidden line.
        var hiddenReasons = digest.SuppressionBreakdown
            .OrderByDescending(t => t.Count)
            .Take(2)
            .Select(t => t.Reason)
            .ToList();

        return new HeaderView(
            digest.Mode,
            digest.TotalNewJobs,
            digest.StrongMatches,
            ToThousands(digest.AvgSalaryUsd),
            digest.CompaniesChecked,
            digest.AnalysedCount,
            cardCount,
            digest.SuppressedCount,
            hiddenReasons,
            digest.CarriedOverCount,
            top);
    }

    private static FooterView BuildFooter(Digest digest) =>
        new(
            digest.SuppressedCount,
            digest.SuppressionBreakdown.Select(t => new FooterTally(t.Count, t.Reason)).ToList(),
            digest.CarriedOverCount,
            digest.DegradedSources,
            digest.LearningEnabled);

    // The single best opportunity promoted into a full header: the rank-1 card, joined to its display facts.
    // Only a full digest with a resolvable top card gets one; the other modes render no opportunity line.
    private static HeaderOpportunity? ResolveTopOpportunity(
        Digest digest, IReadOnlyDictionary<Guid, CardDisplayFacts> displayFacts)
    {
        if (digest.Mode != DigestMode.Full)
        {
            return null;
        }

        var best = digest.Cards.OrderBy(c => c.Rank).FirstOrDefault();
        if (best is null || !displayFacts.TryGetValue(best.JobId, out var display))
        {
            return null;
        }

        return new HeaderOpportunity(display.Title, display.Company, best.Score, display.Highlights);
    }

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

    // The confidence band shown in an (est) salary line. A three-band summary, not the raw decimal: the point
    // is that an estimate is qualified, not that it is precise.
    private static string? ConfidenceBand(decimal? confidence) => confidence switch
    {
        null => null,
        >= 0.7m => "high conf",
        >= 0.4m => "med conf",
        _ => "low conf",
    };

    private static int? ToThousands(decimal? usd) =>
        usd is { } value ? (int)Math.Round(value / 1000m, MidpointRounding.AwayFromZero) : null;
}
