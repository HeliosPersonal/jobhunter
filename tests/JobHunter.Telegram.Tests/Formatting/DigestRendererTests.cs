using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Callbacks;
using JobHunter.Telegram.Formatting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Formatting;

/// <summary>
/// The production <see cref="DigestRenderer"/> (F5 T12): the concrete <see cref="IDigestRenderer"/> both the
/// 07:00 delivery handler and <c>/digest</c> depend on. It turns a persisted <see cref="Digest"/> and the
/// per-job display facts into the ordered, keyed message sequence the delivery loop sends — header, one card
/// per rank, then a footer when it has content. Each card carries the fixed four-button inline keyboard, and
/// its callback buttons carry the HMAC short id so a tap resolves back to the card (T10). The renderer reads
/// only stored digest state and public job facts — never the CV.
/// </summary>
public sealed class DigestRendererTests
{
    private static readonly Guid RunId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid JobA = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid JobB = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 6, 6, 45, 0, TimeSpan.Zero);

    [Fact]
    public async Task Renders_header_then_cards_by_rank_then_footer_each_under_its_key()
    {
        var digest = BuildDigest(
            mode: DigestMode.Full,
            suppressedCount: 3,
            breakdown: [Tally("below salary floor", 3)],
            cards: [Card(JobA, rank: 1, score: 90m), Card(JobB, rank: 2, score: 80m)]);

        var facts = new FakeCardDisplayQuery(new Dictionary<Guid, CardDisplayFacts>
        {
            [JobA] = Facts(JobA, "Staff SRE", publishedMin: 150_000, publishedMax: 180_000),
            [JobB] = Facts(JobB, "Platform Engineer", publishedMin: null, publishedMax: null),
        });

        var messages = await NewRenderer(facts).RenderAsync(digest);

        // Header first, both cards in rank order, footer last — five messages, each under its own key.
        messages.Select(m => m.Key.Value).ShouldBe(
        [
            CardKey.HeaderValue,
            CardKey.For(RunId, JobA).Value,
            CardKey.For(RunId, JobB).Value,
            CardKey.FooterValue,
        ]);
    }

    [Fact]
    public async Task Each_card_carries_the_fixed_four_button_keyboard_with_a_signed_open_url()
    {
        var digest = BuildDigest(
            mode: DigestMode.Full, suppressedCount: 0, breakdown: [],
            cards: [Card(JobA, rank: 1, score: 90m)]);
        var applyUrl = "https://acme.example/apply/1";
        var facts = new FakeCardDisplayQuery(new Dictionary<Guid, CardDisplayFacts>
        {
            [JobA] = Facts(JobA, "Staff SRE", publishedMin: 150_000, publishedMax: 180_000, applyUrl: applyUrl),
        });

        var messages = await NewRenderer(facts).RenderAsync(digest);

        var card = messages.Single(m => m.Key.Value == CardKey.For(RunId, JobA).Value);
        var row = card.Message.Keyboard.ShouldHaveSingleItem();
        row.Select(b => b.Label).ShouldBe(["Open", "Ignore", "Save", "Applied"]);
        // Open is a URL button — the tap never reaches the bot; the other three carry the signed short id.
        row[0].Url.ShouldBe(applyUrl);
        row[0].CallbackData.ShouldBeNull();
        var shortId = new CallbackDataCodec(TestOptions()).Encode(CardKey.For(RunId, JobA));
        row[1].CallbackData.ShouldBe($"ign:{shortId}");
        row[2].CallbackData.ShouldBe($"sav:{shortId}");
        row[3].CallbackData.ShouldBe($"app:{shortId}");
    }

    [Fact]
    public async Task A_card_without_a_published_salary_shows_the_estimate_marked_est()
    {
        var digest = BuildDigest(
            mode: DigestMode.Full, suppressedCount: 0, breakdown: [],
            cards: [Card(JobA, rank: 1, score: 90m)]);
        var facts = new FakeCardDisplayQuery(new Dictionary<Guid, CardDisplayFacts>
        {
            [JobA] = Facts(JobA, "Staff SRE", publishedMin: null, publishedMax: null)
                with { EstimatedSalaryMin = 120_000, EstimatedSalaryMax = 140_000, EstimatedSalaryCurrency = "USD", EstimatedSalaryConfidence = 0.6m },
        });

        var messages = await NewRenderer(facts).RenderAsync(digest);

        var card = messages.Single(m => m.Key.Value == CardKey.For(RunId, JobA).Value);
        card.Message.Text.ShouldContain("(est");
        card.Message.Text.ShouldContain("120");
    }

    [Fact]
    public async Task The_footer_is_omitted_on_a_clean_day()
    {
        var digest = BuildDigest(
            mode: DigestMode.Full, suppressedCount: 0, breakdown: [],
            cards: [Card(JobA, rank: 1, score: 90m)]);
        var facts = new FakeCardDisplayQuery(new Dictionary<Guid, CardDisplayFacts>
        {
            [JobA] = Facts(JobA, "Staff SRE", publishedMin: 150_000, publishedMax: 180_000),
        });

        var messages = await NewRenderer(facts).RenderAsync(digest);

        // Nothing hidden, nothing carried over, nothing degraded — the digest ends on the last card.
        messages.ShouldNotContain(m => m.Key.Value == CardKey.FooterValue);
    }

    [Fact]
    public async Task A_card_whose_job_facts_are_missing_is_skipped_rather_than_fabricated()
    {
        var digest = BuildDigest(
            mode: DigestMode.Full, suppressedCount: 0, breakdown: [],
            cards: [Card(JobA, rank: 1, score: 90m), Card(JobB, rank: 2, score: 80m)]);
        // Only JobA has display facts; JobB is absent from the store.
        var facts = new FakeCardDisplayQuery(new Dictionary<Guid, CardDisplayFacts>
        {
            [JobA] = Facts(JobA, "Staff SRE", publishedMin: 150_000, publishedMax: 180_000),
        });

        var messages = await NewRenderer(facts).RenderAsync(digest);

        messages.ShouldContain(m => m.Key.Value == CardKey.For(RunId, JobA).Value);
        messages.ShouldNotContain(m => m.Key.Value == CardKey.For(RunId, JobB).Value);
    }

    [Fact]
    public async Task Learning_off_surfaces_a_footer_stating_so_even_on_an_otherwise_clean_day()
    {
        // AC-07 end-to-end: a digest assembled while learning was off renders a footer that says so, even
        // though nothing was hidden, carried over or degraded — the Owner is told the ordering was explicit.
        var digest = BuildDigest(
            mode: DigestMode.Full, suppressedCount: 0, breakdown: [],
            cards: [Card(JobA, rank: 1, score: 90m)], learningEnabled: false);
        var facts = new FakeCardDisplayQuery(new Dictionary<Guid, CardDisplayFacts>
        {
            [JobA] = Facts(JobA, "Staff SRE", publishedMin: 150_000, publishedMax: 180_000),
        });

        var messages = await NewRenderer(facts).RenderAsync(digest);

        var footer = messages.Single(m => m.Key.Value == CardKey.FooterValue);
        footer.Message.Text.ShouldContain("learning is off");
    }

    private static DigestRenderer NewRenderer(ICardDisplayQuery facts) =>
        new(facts, new CallbackDataCodec(TestOptions()));

    private static IOptions<TelegramOptions> TestOptions() =>
        Options.Create(new TelegramOptions { BotToken = "test-token", AllowedChatIds = [42] });

    private static Digest BuildDigest(
        DigestMode mode, int suppressedCount, IReadOnlyList<SuppressionTally> breakdown, IReadOnlyList<DigestCard> cards,
        bool learningEnabled = true)
    {
        var digestId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var boundCards = cards.Select(c => new DigestCard(
            c.Id, digestId, c.JobId, RunId, c.Rank, c.Score, c.Reasons, applyUrlVerified: true)).ToList();

        return new Digest(
            digestId, RunId, mode, totalNewJobs: 10, strongMatches: cards.Count, avgSalaryUsd: 185_000m,
            suppressedCount, breakdown, carriedOverCount: 0, companiesChecked: 5, analysedCount: 10,
            degradedSources: [], narrative: null, NarrativeSource.Template, promptVersion: null,
            boundCards, GeneratedAt, restoredCount: 0, learningEnabled: learningEnabled);
    }

    private static DigestCard Card(Guid jobId, int rank, decimal score) =>
        new(Guid.CreateVersion7(), Guid.Parse("44444444-4444-4444-4444-444444444444"), jobId, RunId,
            rank, score, ["Strong platform fit."], applyUrlVerified: true);

    private static SuppressionTally Tally(string reason, int count) =>
        SuppressionTally.TryCreate(reason, count).Value;

    private static CardDisplayFacts Facts(
        Guid jobId, string title, int? publishedMin, int? publishedMax,
        string applyUrl = "https://acme.example/apply") =>
        new(jobId, title, "Acme", "Series B", ["Germany"], "Remote", applyUrl,
            publishedMin, publishedMax, publishedMin is null ? null : "USD",
            EstimatedSalaryMin: null, EstimatedSalaryMax: null, EstimatedSalaryCurrency: null,
            EstimatedSalaryConfidence: null, Highlights: ["Go", "Kubernetes"]);

    private sealed class FakeCardDisplayQuery(IReadOnlyDictionary<Guid, CardDisplayFacts> facts) : ICardDisplayQuery
    {
        public Task<IReadOnlyDictionary<Guid, CardDisplayFacts>> DisplayFactsAsync(
            IReadOnlyCollection<Guid> jobIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, CardDisplayFacts>>(
                jobIds.Where(facts.ContainsKey).ToDictionary(id => id, id => facts[id]));
    }
}
