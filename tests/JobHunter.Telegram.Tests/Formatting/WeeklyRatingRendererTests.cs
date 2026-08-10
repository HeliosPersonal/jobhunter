using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Callbacks;
using JobHunter.Telegram.Formatting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Formatting;

/// <summary>
/// The production <see cref="IWeeklyRatingRenderer"/> (F4 T20): one "was this worth opening?" prompt per
/// delivered top-ten card. The load-bearing properties: the prompt names the role (title and company) so the
/// Owner rates a card they recognise, not an anonymous "#3"; it carries an Open button to re-open the posting
/// and a single affirmative rating button whose callback payload is the self-contained signed job id (so a
/// tap resolves from the payload alone, no time window); and a card whose job has vanished since delivery
/// renders to null and is skipped. Every dynamic value goes through the one MarkdownV2 escape path. Zero
/// network: the display facts are a faked <see cref="ICardDisplayQuery"/>.
/// </summary>
public sealed class WeeklyRatingRendererTests
{
    private const string Secret = "botfather-token-abc123";
    private static readonly Guid RunId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid JobId = new("22222222-2222-2222-2222-222222222222");
    private const string ApplyUrl = "https://apply.example/roles/42";

    private static readonly WeeklyTopCard Card = new(JobId, RunId, Rank: 3);

    private static readonly CallbackDataCodec Codec =
        new(Options.Create(new TelegramOptions { BotToken = Secret, AllowedChatIds = [4242] }));

    private readonly ICardDisplayQuery _facts = Substitute.For<ICardDisplayQuery>();

    private static CardDisplayFacts Facts(string title = "Staff Platform Engineer", string company = "Acme") =>
        new(JobId, title, company, Stage: null, Countries: ["DE"], RemotePolicy: "Remote (EU)",
            ApplyUrl, null, null, null, null, null, null, null, Highlights: []);

    private WeeklyRatingRenderer Build(CardDisplayFacts? facts)
    {
        var map = facts is null
            ? new Dictionary<Guid, CardDisplayFacts>()
            : new Dictionary<Guid, CardDisplayFacts> { [JobId] = facts };
        _facts.DisplayFactsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<Guid, CardDisplayFacts>)map);
        return new WeeklyRatingRenderer(_facts, Codec);
    }

    [Fact]
    public async Task The_prompt_names_the_role_so_the_owner_rates_a_card_they_recognise()
    {
        var rendered = await Build(Facts()).RenderAsync(Card);

        rendered.ShouldNotBeNull();
        rendered!.Text.ShouldContain("Staff Platform Engineer");
        rendered.Text.ShouldContain("Acme");
    }

    [Fact]
    public async Task The_prompt_carries_an_open_button_and_a_single_worth_opening_rating_button()
    {
        var rendered = await Build(Facts()).RenderAsync(Card);

        var row = rendered!.Keyboard.ShouldHaveSingleItem();
        row.Count.ShouldBe(2);
        row[0].Label.ShouldBe("Open");
        row[0].Url.ShouldBe(ApplyUrl);
        row[0].CallbackData.ShouldBeNull();
        row[1].CallbackData.ShouldNotBeNull();
        row[1].CallbackData!.ShouldStartWith("rat:");
    }

    [Fact]
    public async Task The_rating_button_payload_resolves_back_to_the_job_id()
    {
        var rendered = await Build(Facts()).RenderAsync(Card);

        var payload = rendered!.Keyboard[0][1].CallbackData!["rat:".Length..];
        Codec.ResolveRating(payload).ShouldBe(JobId);
    }

    [Fact]
    public async Task A_card_whose_job_has_vanished_renders_to_null()
    {
        (await Build(facts: null).RenderAsync(Card)).ShouldBeNull();
    }

    [Fact]
    public async Task A_title_full_of_markdown_specials_is_escaped()
    {
        var rendered = await Build(Facts(title: "C++ (Senior) *Lead*")).RenderAsync(Card);

        // Every MarkdownV2 special is backslash-escaped, so the send cannot silently fail.
        rendered!.Text.ShouldContain("C\\+\\+ \\(Senior\\) \\*Lead\\*");
    }

    [Fact]
    public async Task A_null_card_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => Build(Facts()).RenderAsync(null!));
    }

    [Fact]
    public void Null_dependencies_are_rejected()
    {
        Should.Throw<ArgumentNullException>(() => new WeeklyRatingRenderer(null!, Codec));
        Should.Throw<ArgumentNullException>(() => new WeeklyRatingRenderer(_facts, null!));
    }
}
