using JobHunter.Telegram.Formatting;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Formatting;

/// <summary>
/// The canonical escaper (F5 message contract §Escaping). Every MarkdownV2 special character is
/// backslash-escaped, because one unescaped special silently fails the whole send; truncation counts
/// graphemes, not bytes, so a flag emoji or a CJK glyph at the boundary is never split (the rendering
/// corpus's hostile-input rules).
/// </summary>
public sealed class MarkdownV2EscaperTests
{
    [Theory]
    [InlineData('_')]
    [InlineData('*')]
    [InlineData('[')]
    [InlineData(']')]
    [InlineData('(')]
    [InlineData(')')]
    [InlineData('~')]
    [InlineData('`')]
    [InlineData('>')]
    [InlineData('#')]
    [InlineData('+')]
    [InlineData('-')]
    [InlineData('=')]
    [InlineData('|')]
    [InlineData('{')]
    [InlineData('}')]
    [InlineData('.')]
    [InlineData('!')]
    public void Every_markdownV2_special_character_is_backslash_escaped(char special)
    {
        MarkdownV2Escaper.Escape(special.ToString()).ShouldBe("\\" + special);
    }

    [Fact]
    public void A_non_special_character_passes_through_unchanged()
    {
        MarkdownV2Escaper.Escape("Senior Engineer").ShouldBe("Senior Engineer");
    }

    [Fact]
    public void A_hostile_markup_string_is_neutralised_entirely()
    {
        MarkdownV2Escaper.Escape("*bold* [link](http://evil)")
            .ShouldBe(@"\*bold\* \[link\]\(http://evil\)");
    }

    [Fact]
    public void A_backtick_and_newline_are_escaped_without_losing_the_text()
    {
        // The backtick is a special; the newline is not markup and survives verbatim.
        MarkdownV2Escaper.Escape("a`b\nc").ShouldBe("a\\`b\nc");
    }

    [Fact]
    public void Null_and_empty_escape_to_the_empty_string()
    {
        MarkdownV2Escaper.Escape(null).ShouldBe(string.Empty);
        MarkdownV2Escaper.Escape(string.Empty).ShouldBe(string.Empty);
    }

    [Fact]
    public void An_emoji_passes_through_unbroken()
    {
        MarkdownV2Escaper.Escape("Acme 🚀").ShouldBe("Acme 🚀");
    }

    [Fact]
    public void A_short_value_is_returned_unchanged_by_truncate()
    {
        MarkdownV2Escaper.Truncate("Senior Engineer", 60).ShouldBe("Senior Engineer");
    }

    [Fact]
    public void A_long_multi_word_value_is_truncated_at_a_word_boundary_with_an_ellipsis()
    {
        var title = string.Join(' ', Enumerable.Repeat("Senior", 30));

        var truncated = MarkdownV2Escaper.Truncate(title, 60);

        truncated.ShouldEndWith("…");
        truncated.ShouldNotContain("Senior Senior Senior Senior Senior Senior Senior Senior Senior Senior Senior");
        // Backed off to a word boundary, so it never ends on a partial word before the ellipsis.
        truncated.ShouldNotContain("Seni…");
    }

    [Fact]
    public void A_single_long_word_with_no_boundary_is_still_truncated()
    {
        var truncated = MarkdownV2Escaper.Truncate(new string('x', 200), 60);

        truncated.ShouldEndWith("…");
        truncated.Length.ShouldBeLessThan(200);
    }

    [Fact]
    public void Truncation_counts_graphemes_so_a_flag_emoji_at_the_boundary_is_not_split()
    {
        // Each regional-indicator flag is two UTF-16 code units but one grapheme. A run of flags past the
        // cap must be cut on a whole flag, never mid-surrogate — the result stays a valid string.
        var flags = string.Concat(Enumerable.Repeat("🇺🇦", 10));

        var truncated = MarkdownV2Escaper.Truncate(flags, 3);

        // No lone surrogate survived the cut (a split flag would leave one).
        truncated.ShouldNotContain('�');
        char.IsLowSurrogate(truncated[^1]).ShouldBeFalse();
    }

    [Fact]
    public void Truncation_counts_graphemes_for_a_cjk_title()
    {
        var title = string.Concat(Enumerable.Repeat("株", 100));

        var truncated = MarkdownV2Escaper.Truncate(title, 60);

        truncated.ShouldEndWith("…");
    }

    [Fact]
    public void Truncate_rejects_a_non_positive_length()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => MarkdownV2Escaper.Truncate("x", 0));
    }

    [Fact]
    public void Truncate_of_null_or_empty_is_empty()
    {
        MarkdownV2Escaper.Truncate(null, 10).ShouldBe(string.Empty);
        MarkdownV2Escaper.Truncate(string.Empty, 10).ShouldBe(string.Empty);
    }

    [Theory]
    [InlineData(150000, "150k")]
    [InlineData(185000, "185k")]
    [InlineData(950, "950")]
    [InlineData(0, "0")]
    public void FormatThousands_abbreviates_from_a_thousand_and_shows_small_amounts_verbatim(int amount, string expected)
    {
        MarkdownV2Escaper.FormatThousands(amount).ShouldBe(expected);
    }

    [Fact]
    public void Link_escapes_the_label_and_wraps_it_in_an_inline_link()
    {
        MarkdownV2Escaper.Link("1 Aug 2026", "https://acme.ai/press")
            .ShouldBe(@"[1 Aug 2026](https://acme.ai/press)");
    }

    [Fact]
    public void Link_escapes_a_closing_paren_in_the_url_so_it_cannot_terminate_the_link_early()
    {
        MarkdownV2Escaper.Link("source", "https://x.example/a(b)c")
            .ShouldBe(@"[source](https://x.example/a(b\)c)");
    }

    [Fact]
    public void Link_degrades_to_the_plain_escaped_label_when_the_url_is_blank()
    {
        MarkdownV2Escaper.Link("a.b", null).ShouldBe(@"a\.b");
        MarkdownV2Escaper.Link("a.b", "   ").ShouldBe(@"a\.b");
    }
}
