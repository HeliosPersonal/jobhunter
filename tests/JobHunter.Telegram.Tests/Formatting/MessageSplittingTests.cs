using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Callbacks;
using JobHunter.Telegram.Formatting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Formatting;

/// <summary>
/// The splitting-boundary suite (F5 test-plan §The rendering corpus → Splitting; message contract §Message
/// limits). Telegram caps a message at 4096 characters and the contract's rule is that the digest "splits at
/// a card boundary, never mid-card". In this design there is no post-hoc splitter: the
/// <see cref="DigestRenderer"/> emits <em>one <see cref="RenderableMessage"/> per header, card and footer</em>
/// (contract §Card — "Cards are sent as separate messages"), and the notifier sends each atomically. So the
/// boundary is structural: a card is never fragmented because it is a whole message, and no single message can
/// approach 4096 because the formatters cap the title (60 graphemes), the reasons (three × 90) and the header
/// (six lines). This suite proves both — a maximal card and header stay well under the limit, and a digest
/// whose <em>total</em> content crosses 4096 is delivered as many whole-card messages, each within the limit —
/// so splitting is asserted just under, at (a maximal single message) and just over the boundary.
/// </summary>
public sealed class MessageSplittingTests
{
    private const int TelegramLimit = 4096;
    private static readonly Guid RunId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid DigestId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 6, 6, 45, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_maximally_long_card_still_fits_in_one_message_well_under_the_limit()
    {
        // The longest card the formatter can emit: a 60-grapheme title, a long company, stage and location,
        // three 90-grapheme reasons and a salary line. If even this is under 4096, a card never needs a split.
        var maximal = MaximalCard();
        var facts = new GeneratingFacts(maxima: true);

        var messages = await NewRenderer(facts).RenderAsync(BuildDigest([maximal]));

        var card = messages.Single(m => m.Key.Value == maximal.Key.Value);
        card.Message.Text.Length.ShouldBeLessThan(TelegramLimit);
        card.Message.Text.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_maximally_long_header_still_fits_in_one_message_under_the_limit()
    {
        // A full-mode header with a long top opportunity and every count populated is still six lines: bounded.
        var facts = new GeneratingFacts(maxima: true);

        var messages = await NewRenderer(facts).RenderAsync(BuildDigest([MaximalCard()]));

        var header = messages.Single(m => m.Key.Value == CardKey.HeaderValue);
        header.Message.Text.Length.ShouldBeLessThan(TelegramLimit);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(40)]
    public async Task No_single_message_ever_reaches_the_4096_char_limit(int cardCount)
    {
        var facts = new GeneratingFacts(maxima: true);

        var messages = await NewRenderer(facts).RenderAsync(BuildDigest(Cards(cardCount)));

        messages.ShouldAllBe(m => m.Message.Text.Length < TelegramLimit);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public async Task Each_card_is_its_own_message_so_a_split_only_ever_falls_between_cards(int cardCount)
    {
        var cards = Cards(cardCount);
        var facts = new GeneratingFacts(maxima: false);

        var messages = await NewRenderer(facts).RenderAsync(BuildDigest(cards));

        // Header + one message per card, in rank order, no footer on this clean digest — the "twelve messages
        // for ten cards" property (minus the footer), which is only possible if a card is never fragmented.
        messages.Count.ShouldBe(cardCount + 1);
        var cardMessages = messages.Where(m => m.Key.Value != CardKey.HeaderValue).ToList();
        cardMessages.Count.ShouldBe(cardCount);
        cardMessages.Select(m => m.Key.Value).Distinct().Count().ShouldBe(cardCount);

        // No message carries two cards' titles: a mid-card split (or a merge) would put "Role 001" and
        // "Role 002" into one message. Each message shows at most one card's title (the header may echo the
        // rank-1 card as the top opportunity, which is still a single title).
        foreach (var message in messages)
        {
            var titlesPresent = cards.Count(c => message.Message.Text.Contains(TitleOf(c.Rank), StringComparison.Ordinal));
            titlesPresent.ShouldBeLessThanOrEqualTo(1);
        }
    }

    [Fact]
    public async Task A_digest_whose_total_content_exceeds_4096_is_split_into_whole_card_messages()
    {
        // Forty maximal cards is far more than one 4096-char message could hold, so the digest genuinely
        // crosses the limit — the case the splitting rule is about.
        var cards = Cards(40);
        var facts = new GeneratingFacts(maxima: true);

        var messages = await NewRenderer(facts).RenderAsync(BuildDigest(cards));

        // The whole digest crosses 4096 many times over...
        messages.Sum(m => m.Message.Text.Length).ShouldBeGreaterThan(TelegramLimit);
        // ...yet no single message does, because the break is always at a card boundary, never inside a card.
        messages.ShouldAllBe(m => m.Message.Text.Length <= TelegramLimit);
        messages.Count(m => m.Key.Value != CardKey.HeaderValue).ShouldBe(40);
    }

    [Fact]
    public async Task A_digest_whose_total_content_is_under_4096_is_still_one_message_per_card()
    {
        var cards = Cards(3);
        var facts = new GeneratingFacts(maxima: false);

        var messages = await NewRenderer(facts).RenderAsync(BuildDigest(cards));

        // A small digest sits under the limit in aggregate, and is still one whole message per card.
        messages.Sum(m => m.Message.Text.Length).ShouldBeLessThan(TelegramLimit);
        messages.Count(m => m.Key.Value != CardKey.HeaderValue).ShouldBe(3);
    }

    private static DigestRenderer NewRenderer(ICardDisplayQuery facts) =>
        new(facts, new CallbackDataCodec(
            Options.Create(new TelegramOptions { BotToken = "test-token", AllowedChatIds = [42] })));

    private static List<DigestCard> Cards(int count) =>
        Enumerable.Range(1, count).Select(rank => Card(JobId(rank), rank)).ToList();

    private static DigestCard MaximalCard() => Card(JobId(1), rank: 1);

    private static DigestCard Card(Guid jobId, int rank) =>
        new(Guid.CreateVersion7(), DigestId, jobId, RunId, rank, score: 90m,
            reasons: [new string('r', 100), new string('s', 100), new string('t', 100)],
            applyUrlVerified: true);

    private static Digest BuildDigest(List<DigestCard> cards) =>
        new(
            DigestId, RunId, DigestMode.Full, totalNewJobs: 999, strongMatches: cards.Count,
            avgSalaryUsd: 185_000m, suppressedCount: 0, suppressionBreakdown: [], carriedOverCount: 0,
            companiesChecked: 500, analysedCount: 999, degradedSources: [], narrative: null,
            NarrativeSource.Template, promptVersion: null, cards, GeneratedAt);

    private static Guid JobId(int rank) =>
        Guid.Parse($"00000000-0000-0000-0000-{rank:D12}");

    private static string TitleOf(int rank) => $"Role {rank:D3}";

    // Generates display facts for any requested card job id, so a digest of any size can be rendered without a
    // database. In "maxima" mode every field is as long as the formatter allows, to push a card message toward
    // (but never past) the 4096-char limit; otherwise the facts are short and identifiable per rank.
    private sealed class GeneratingFacts(bool maxima) : ICardDisplayQuery
    {
        public Task<IReadOnlyDictionary<Guid, CardDisplayFacts>> DisplayFactsAsync(
            IReadOnlyCollection<Guid> jobIds, CancellationToken cancellationToken = default)
        {
            var facts = jobIds.ToDictionary(id => id, Build);
            return Task.FromResult<IReadOnlyDictionary<Guid, CardDisplayFacts>>(facts);
        }

        private CardDisplayFacts Build(Guid jobId)
        {
            var rank = int.Parse(jobId.ToString("N")[^12..], System.Globalization.CultureInfo.InvariantCulture);
            var title = maxima
                ? TitleOf(rank) + " " + new string('X', CardFormatter.MaxTitleGraphemes)
                : TitleOf(rank);
            var company = maxima ? new string('C', 80) : "Acme";
            var location = maxima ? new string('L', 80) : "Remote";

            return new CardDisplayFacts(
                jobId, title, company, "Series B", [location], "Remote",
                $"https://acme.example/apply/{rank}",
                PublishedSalaryMin: 150_000, PublishedSalaryMax: 190_000, PublishedSalaryCurrency: "USD",
                EstimatedSalaryMin: null, EstimatedSalaryMax: null, EstimatedSalaryCurrency: null,
                EstimatedSalaryConfidence: null, Highlights: ["Go", "Kubernetes"]);
        }
    }
}
