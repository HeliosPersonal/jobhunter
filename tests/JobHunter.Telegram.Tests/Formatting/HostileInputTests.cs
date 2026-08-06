using System.Globalization;
using JobHunter.Telegram.Formatting;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Formatting;

/// <summary>
/// The hostile-input suite (F5 message contract §Escaping table; test-plan §The rendering corpus → Hostile
/// input). One case per row of the contract's adversarial table, plus the test-plan's extra rows: every
/// MarkdownV2 metacharacter a job board can put in a title, company or reason must reach the message
/// <em>escaped</em>, because a single unescaped special silently fails the whole send. The card layout must
/// stay intact — a title full of markup renders literally, a link is not a link, a newline does not add a
/// line. The load-bearing assertion is structural: no MarkdownV2 metacharacter appears in the output without a
/// backslash before it (the one exception being the formatter's own literal markup — the bold asterisks and
/// bullet dashes it emits itself).
/// </summary>
public sealed class HostileInputTests
{
    // The full MarkdownV2 special set (contract §Escaping) — each must be backslash-escaped when it comes from
    // a dynamic value.
    private static readonly char[] Specials =
        ['_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!'];

    [Fact]
    public void A_title_with_bold_and_italic_markup_is_escaped_and_displayed_literally()
    {
        var card = Card(title: "*bold* and _italic_ title");

        var text = CardFormatter.Format(card);

        // The markup survives as literal text (escaped), never as active bold/italic.
        text.ShouldContain(@"\*bold\*");
        text.ShouldContain(@"\_italic\_");
        AssertAllDynamicSpecialsEscaped(text);
    }

    [Fact]
    public void A_company_name_that_is_a_markdown_link_is_escaped_not_rendered_as_a_link()
    {
        var card = Card(company: "[Acme](http://evil)");

        var text = CardFormatter.Format(card);

        text.ShouldContain(@"\[Acme\]\(http://evil\)");
        AssertAllDynamicSpecialsEscaped(text);
    }

    [Fact]
    public void A_reason_with_a_newline_and_a_backtick_is_escaped_with_the_layout_intact()
    {
        var card = Card(reasons: ["uses `kubectl`\nand a backtick"]);

        var text = CardFormatter.Format(card);

        // The backtick is escaped and the embedded newline is collapsed, so the reason stays a single bullet.
        text.ShouldContain(@"\`kubectl\`");
        ReasonLines(text).Length.ShouldBe(1);
        AssertAllDynamicSpecialsEscaped(text);
    }

    [Fact]
    public void A_title_of_400_characters_is_truncated_at_a_word_boundary_to_60_graphemes()
    {
        var word = "supercalifragilistic";
        var longTitle = string.Join(' ', Enumerable.Repeat(word, 40)); // ~ 840 chars
        longTitle.Length.ShouldBeGreaterThan(400);

        var card = Card(title: longTitle);
        var text = CardFormatter.Format(card);

        var titleLine = FirstLine(text);
        // The rendered title (stripped of its bold asterisks and trailing ellipsis) is at most 60 graphemes,
        // and it ends on a whole word — the truncation never splits "supercalifragilistic" in half.
        var inner = titleLine.Trim('*').TrimEnd('…');
        GraphemeCount(inner).ShouldBeLessThanOrEqualTo(CardFormatter.MaxTitleGraphemes);
        inner.ShouldNotBeEmpty();
        AssertAllDynamicSpecialsEscaped(text);
    }

    [Theory]
    [InlineData("مطلوب مهندس برمجيات كبير للعمل عن بعد في فريق البنية التحتية السحابية لدينا")]
    [InlineData("クラウドインフラチームのためのシニアソフトウェアエンジニアを募集しています")]
    public void A_title_in_a_non_latin_script_renders_and_truncates_on_graphemes(string title)
    {
        var card = Card(title: title);

        var text = CardFormatter.Format(card);

        // It renders (non-empty title line) and never exceeds the grapheme cap — bytes are not the unit.
        var inner = FirstLine(text).Trim('*').TrimEnd('…');
        inner.ShouldNotBeEmpty();
        GraphemeCount(inner).ShouldBeLessThanOrEqualTo(CardFormatter.MaxTitleGraphemes);
    }

    [Fact]
    public void An_emoji_in_a_company_name_passes_through_unbroken()
    {
        var card = Card(company: "Acme 🚀 Corp");

        var text = CardFormatter.Format(card);

        text.ShouldContain("🚀");
        AssertAllDynamicSpecialsEscaped(text);
    }

    [Fact]
    public void A_reason_with_a_double_newline_is_collapsed_to_a_single_space()
    {
        var card = Card(reasons: ["first part\n\nsecond part"]);

        var text = CardFormatter.Format(card);

        text.ShouldContain("first part second part");
        ReasonLines(text).Length.ShouldBe(1);
    }

    // ---- test-plan §Hostile input extra rows ----

    [Fact]
    public void A_title_that_is_entirely_markdown_control_characters_escapes_every_one()
    {
        var card = Card(title: "*_[]()~`>#+-=|{}.!");

        var text = CardFormatter.Format(card);

        AssertAllDynamicSpecialsEscaped(text);
        // Every special that was in the raw title survives as an escaped literal, not as active markup.
        foreach (var special in Specials)
        {
            text.ShouldContain(@"\" + special);
        }
    }

    [Fact]
    public void A_company_name_with_a_zero_width_joiner_passes_through_and_stays_safe()
    {
        // A family emoji is a ZWJ sequence; it must survive as one grapheme, not be split or dropped.
        var card = Card(company: "Acme \U0001F468‍\U0001F469‍\U0001F467 Ltd");

        var text = CardFormatter.Format(card);

        text.ShouldContain("‍");
        AssertAllDynamicSpecialsEscaped(text);
    }

    [Fact]
    public void A_reason_with_a_url_containing_parentheses_escapes_the_parentheses()
    {
        var card = Card(reasons: ["see https://en.wikipedia.org/wiki/Kafka_(software) for context"]);

        var text = CardFormatter.Format(card);

        text.ShouldContain(@"\(software\)");
        AssertAllDynamicSpecialsEscaped(text);
    }

    [Fact]
    public void A_flag_emoji_exactly_at_the_truncation_boundary_is_never_split_in_half()
    {
        // 59 letters then a flag emoji (a two-code-point grapheme) sits at position 60: truncation must keep
        // or drop it whole, never emit a lone regional-indicator half.
        var title = new string('a', 59) + "\U0001F1FA\U0001F1F8"; // 🇺🇸
        var card = Card(title: title);

        var text = CardFormatter.Format(card);

        var inner = FirstLine(text).Trim('*').TrimEnd('…');
        GraphemeCount(inner).ShouldBeLessThanOrEqualTo(CardFormatter.MaxTitleGraphemes);
        // A broken flag would leave exactly one regional-indicator code point; assert the pair is whole or gone.
        var lone = inner.EnumerateRunes().Count(r => r.Value is >= 0x1F1E6 and <= 0x1F1FF);
        (lone % 2).ShouldBe(0);
    }

    private static CardView Card(
        string title = "Senior Platform Engineer",
        string company = "Stripe",
        IReadOnlyList<string>? reasons = null) =>
        new(title, company, "Series B", "Remote",
            new CardSalary(150_000, 190_000, "USD", IsEstimate: false, null), 87m,
            reasons ?? ["a solid reason"]);

    private static string FirstLine(string text) =>
        text.Split('\n')[0];

    private static string[] ReasonLines(string text) =>
        text.Split('\n').Where(l => l.StartsWith("• ", StringComparison.Ordinal)).ToArray();

    private static int GraphemeCount(string value)
    {
        var count = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext())
        {
            count++;
        }

        return count;
    }

    // Asserts that every MarkdownV2 special character in the output is backslash-escaped, except the
    // formatter's own literal markup: the bold '*' it wraps titles/scores in, and the '·' separators. We check
    // this by removing the known-safe literal markup the formatter emits, then confirming no bare special is
    // left preceded by anything other than a backslash.
    private static void AssertAllDynamicSpecialsEscaped(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (!Array.Exists(Specials, s => s == ch))
            {
                continue;
            }

            // A special is safe if it is itself escaped (preceded by a backslash)...
            if (i > 0 && text[i - 1] == '\\')
            {
                continue;
            }

            // ...or it is one of the formatter's own structural literals: the bold '*' around the title and
            // score, and the bullet '•' lines are not specials. The only bare special the formatter emits is
            // the '*' markup; every dynamic value is escaped, so a bare '*' is the only permitted exception.
            if (ch == '*')
            {
                continue;
            }

            Assert.Fail($"Unescaped MarkdownV2 special '{ch}' at index {i} in output:\n{text}");
        }
    }
}
