using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Callbacks;

namespace JobHunter.Telegram.Formatting;

/// <summary>
/// The production <see cref="IWeeklyRatingRenderer"/> (F4 T20) the weekly precision@10 loop sends through. It
/// turns one delivered top-ten <see cref="WeeklyTopCard"/> into the single "was this worth opening?" prompt
/// the Owner rates. The prompt <em>names the role</em> — title and company, joined fresh through
/// <see cref="ICardDisplayQuery"/> — so the Owner rates a card they recognise rather than an anonymous rank,
/// which is what makes the resulting precision figure meaningful.
///
/// <para>The keyboard is two buttons: Open, a URL button that re-opens the posting so the Owner can judge it,
/// and a single affirmative "worth opening" callback button. Only the affirmative is offered because
/// precision@10 is <c>(cards rated worth opening) / (delivered top-ten)</c> (D5) — the denominator is fixed
/// at delivery, so <em>not</em> tapping is the "not worth" answer and needs no button. The button's payload
/// is the self-contained signed job id (<see cref="CallbackDataCodec.EncodeRating"/>), not the digest's
/// windowed short id: a weekly prompt is rated up to a week late, so the tap must resolve from the payload
/// alone and never fall out of a sliding resolution window.</para>
///
/// <para>A card whose job has gone between delivery and the rating round renders to <c>null</c> — the caller
/// skips it; the card still counts in the delivered denominator but contributes no prompt. Every dynamic
/// value passes through <see cref="MarkdownV2Escaper"/>, the one escape path, so a title full of specials
/// cannot silently fail the send. It reads only public job facts — the CV crosses exactly one boundary, and
/// it is not this one.</para>
/// </summary>
internal sealed class WeeklyRatingRenderer(ICardDisplayQuery facts, CallbackDataCodec codec) : IWeeklyRatingRenderer
{
    // The rating callback action token, alongside the contract's open|ign|sav|app card tokens.
    private const string RateToken = "rat";

    /// <summary>Titles longer than this are truncated at a word boundary, as the digest cards are.</summary>
    private const int MaxTitleGraphemes = 80;

    private readonly ICardDisplayQuery _facts = facts ?? throw new ArgumentNullException(nameof(facts));
    private readonly CallbackDataCodec _codec = codec ?? throw new ArgumentNullException(nameof(codec));

    public async Task<RenderedMessage?> RenderAsync(WeeklyTopCard card, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);

        var displayFacts = await _facts.DisplayFactsAsync([card.JobId], cancellationToken).ConfigureAwait(false);
        if (!displayFacts.TryGetValue(card.JobId, out var display))
        {
            // The job has gone since delivery — nothing to show, so the caller skips this card.
            return null;
        }

        var title = MarkdownV2Escaper.Escape(MarkdownV2Escaper.Truncate(display.Title, MaxTitleGraphemes));
        var company = MarkdownV2Escaper.Escape(display.Company);
        // Adjacent constants around already-escaped values (rule 9): never interpolate a raw value next to
        // active markup, or one unescaped special silently fails the whole send.
        var subject = "*" + title + "* at " + company;
        var text = string.Join("\n", ["⭐ " + subject, MarkdownV2Escaper.Escape("Was this worth opening?")]);

        var ratingPayload = _codec.EncodeRating(card.JobId);
        IReadOnlyList<IReadOnlyList<InlineButton>> keyboard =
        [
            [
                InlineButton.ForUrl("Open", display.ApplyUrl),
                new InlineButton("👍 Worth opening", $"{RateToken}:{ratingPayload}"),
            ],
        ];

        return new RenderedMessage(text, keyboard);
    }
}
