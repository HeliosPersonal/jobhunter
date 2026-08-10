using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Callbacks;
using JobHunter.Telegram.Formatting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Formatting;

/// <summary>
/// The composition arms of the production <see cref="DigestRenderer"/> (F5 T12): the estimate's confidence
/// band summarised three ways — high, med or low — the location falling back to the remote policy when a card
/// names no country, and the header's top-opportunity line, which only a full digest with a resolvable rank-1
/// card gets — every other mode, and a full digest whose best card has no display facts, renders none.
/// Assertion-based against fresh digests, so the committed rendering-corpus snapshots stay untouched.
/// </summary>
public sealed class DigestRendererBranchTests
{
    private static readonly Guid RunId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid JobA = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid JobB = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 6, 6, 45, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0.8, "high conf")]
    [InlineData(0.5, "med conf")]
    [InlineData(0.2, "low conf")]
    public async Task An_estimate_confidence_is_summarised_as_a_three_way_band(double confidence, string expected)
    {
        var digest = FullDigest(Card(JobA, rank: 1, score: 90m));
        var facts = Query(
            (JobA, Facts(JobA, "Staff SRE", publishedMin: null, publishedMax: null) with
            {
                EstimatedSalaryMin = 120_000,
                EstimatedSalaryMax = 140_000,
                EstimatedSalaryCurrency = "USD",
                EstimatedSalaryConfidence = (decimal)confidence,
            }));

        var card = await CardTextAsync(digest, facts);

        card.ShouldContain("(est");
        card.ShouldContain(expected);
    }

    [Fact]
    public async Task An_estimate_with_no_confidence_is_marked_est_with_no_band()
    {
        var digest = FullDigest(Card(JobA, rank: 1, score: 90m));
        var facts = Query(
            (JobA, Facts(JobA, "Staff SRE", publishedMin: null, publishedMax: null) with
            {
                EstimatedSalaryMin = 120_000,
                EstimatedSalaryMax = 140_000,
                EstimatedSalaryCurrency = "USD",
                EstimatedSalaryConfidence = null,
            }));

        var card = await CardTextAsync(digest, facts);

        card.ShouldContain("(est");
        card.ShouldNotContain("conf");
    }

    [Fact]
    public async Task A_card_with_no_countries_falls_back_to_the_remote_policy_for_location()
    {
        var digest = FullDigest(Card(JobA, rank: 1, score: 90m));
        var facts = Query(
            (JobA, Facts(JobA, "Staff SRE", publishedMin: 150_000, publishedMax: 180_000) with
            {
                Countries = [],
                RemotePolicy = "Remote EMEA",
            }));

        var card = await CardTextAsync(digest, facts);

        card.ShouldContain("Remote EMEA");
    }

    [Fact]
    public async Task A_full_digest_promotes_the_rank_one_card_into_a_header_opportunity_line()
    {
        var digest = FullDigest(Card(JobB, rank: 2, score: 80m), Card(JobA, rank: 1, score: 95m));
        var facts = Query(
            (JobA, Facts(JobA, "Principal Platform Engineer", publishedMin: 150_000, publishedMax: 180_000)),
            (JobB, Facts(JobB, "Staff SRE", publishedMin: 120_000, publishedMax: 150_000)));

        var header = await HeaderTextAsync(digest, facts);

        // The rank-1 card, not the first listed, is promoted into the header's single best opportunity.
        header.ShouldContain("Principal Platform Engineer");
    }

    [Theory]
    [InlineData(DigestMode.NothingNew)]
    [InlineData(DigestMode.Partial)]
    [InlineData(DigestMode.BudgetReached)]
    public async Task A_non_full_digest_renders_no_top_opportunity_line(DigestMode mode)
    {
        var digest = BuildDigest(mode, [Card(JobA, rank: 1, score: 90m)]);
        var facts = Query((JobA, Facts(JobA, "Staff SRE", publishedMin: 150_000, publishedMax: 180_000)));

        var header = await HeaderTextAsync(digest, facts);

        // Only the full morning digest carries the opportunity line; the reduced modes never do.
        header.ShouldNotContain("Staff SRE");
    }

    [Fact]
    public async Task A_full_digest_whose_best_card_has_no_facts_renders_no_opportunity_line()
    {
        var digest = FullDigest(Card(JobA, rank: 1, score: 90m));
        // The rank-1 job is absent from the store, so the opportunity cannot be resolved.
        var facts = Query();

        var messages = await NewRenderer(facts).RenderAsync(digest);

        // No cards survive and no opportunity resolves; the header is still rendered under its own key.
        messages.ShouldContain(m => m.Key.Value == CardKey.HeaderValue);
        messages.ShouldNotContain(m => m.Key.Value == CardKey.For(RunId, JobA).Value);
    }

    private static async Task<string> CardTextAsync(Digest digest, ICardDisplayQuery facts)
    {
        var messages = await NewRenderer(facts).RenderAsync(digest);
        return messages.Single(m => m.Key.Value == CardKey.For(RunId, JobA).Value).Message.Text;
    }

    private static async Task<string> HeaderTextAsync(Digest digest, ICardDisplayQuery facts)
    {
        var messages = await NewRenderer(facts).RenderAsync(digest);
        return messages.Single(m => m.Key.Value == CardKey.HeaderValue).Message.Text;
    }

    private static DigestRenderer NewRenderer(ICardDisplayQuery facts) =>
        new(facts, new CallbackDataCodec(TestOptions()));

    private static IOptions<TelegramOptions> TestOptions() =>
        Options.Create(new TelegramOptions { BotToken = "test-token", AllowedChatIds = [42] });

    private static FakeCardDisplayQuery Query(params (Guid Key, CardDisplayFacts Value)[] entries) =>
        new(entries.ToDictionary(e => e.Key, e => e.Value));

    private static Digest FullDigest(params DigestCard[] cards) => BuildDigest(DigestMode.Full, cards);

    private static Digest BuildDigest(DigestMode mode, DigestCard[] cards)
    {
        var digestId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        return new Digest(
            digestId, RunId, mode, totalNewJobs: 10, strongMatches: cards.Length, avgSalaryUsd: 185_000m,
            suppressedCount: 0, suppressionBreakdown: [], carriedOverCount: 0, companiesChecked: 5,
            analysedCount: 10, degradedSources: [], narrative: null, NarrativeSource.Template,
            promptVersion: null, cards, GeneratedAt, restoredCount: 0, learningEnabled: true);
    }

    private static DigestCard Card(Guid jobId, int rank, decimal score) =>
        new(Guid.CreateVersion7(), Guid.Parse("44444444-4444-4444-4444-444444444444"), jobId, RunId,
            rank, score, ["Strong platform fit."], applyUrlVerified: true);

    private static CardDisplayFacts Facts(Guid jobId, string title, int? publishedMin, int? publishedMax) =>
        new(jobId, title, "Acme", "Series B", ["Germany"], "Remote", "https://acme.example/apply",
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
