using JobHunter.Telegram.Formatting;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Formatting;

/// <summary>
/// The card layout (F5 message contract §Card). Load-bearing behaviours: the title is bold and truncated to
/// 60 graphemes at a word boundary; the company line drops an <c>Unknown</c> stage; a salary is shown as a
/// range and an <em>estimate is never presented as fact</em>; the score is a whole number; exactly three
/// reasons render, each de-newlined and capped; and every dynamic value is MarkdownV2-escaped so hostile
/// markup renders literally and can never break the send.
/// </summary>
public sealed class CardFormatterTests
{
    [Fact]
    public void A_full_card_renders_title_company_salary_score_and_three_reasons()
    {
        var rendered = CardFormatter.Format(Card());

        rendered.ShouldContain("*Senior Platform Engineer*");
        rendered.ShouldContain("Stripe");
        rendered.ShouldContain(@"Series\-public"); // the stage's hyphen is escaped
        rendered.ShouldContain("Dublin / Remote EMEA");
        rendered.ShouldContain("💰");
        rendered.ShouldContain("150–190k EUR");
        rendered.ShouldContain("🎯 *87*");
        rendered.ShouldContain("• 7 yrs Kafka against a role naming Kafka as core");
        rendered.ShouldContain("• EMEA timezone, no overlap requirement");
    }

    [Fact]
    public void An_estimated_salary_is_marked_est_with_its_confidence_and_never_as_fact()
    {
        var rendered = CardFormatter.Format(Card(salary: new CardSalary(150000, 190000, "EUR", IsEstimate: true, "med conf")));

        // (est, med conf) — the parentheses and the comma are escaped; it is never presented as a plain figure.
        rendered.ShouldContain(@"\(est, med conf\)");
    }

    [Fact]
    public void A_published_salary_carries_no_estimate_marker()
    {
        var rendered = CardFormatter.Format(Card(salary: new CardSalary(150000, 190000, "EUR", IsEstimate: false, null)));

        rendered.ShouldNotContain("est");
    }

    [Fact]
    public void A_card_with_no_salary_still_shows_the_score()
    {
        var card = Card() with { Salary = null };

        var rendered = CardFormatter.Format(card);

        rendered.ShouldNotContain("💰");
        rendered.ShouldContain("🎯 *87*");
    }

    [Fact]
    public void An_unknown_stage_is_omitted_rather_than_shown_literally()
    {
        var rendered = CardFormatter.Format(Card(stage: "Unknown"));

        rendered.ShouldNotContain("Unknown");
        rendered.ShouldContain("Stripe · Dublin / Remote EMEA");
    }

    [Fact]
    public void A_missing_stage_is_omitted()
    {
        var rendered = CardFormatter.Format(Card(stage: null));

        rendered.ShouldContain("Stripe · Dublin / Remote EMEA");
    }

    [Fact]
    public void A_hostile_title_is_escaped_and_displayed_literally()
    {
        var rendered = CardFormatter.Format(Card(title: "*bold* [link](http://evil)"));

        rendered.ShouldContain(@"\*bold\*");
        rendered.ShouldContain(@"\[link\]\(http://evil\)");
    }

    [Fact]
    public void A_company_name_with_a_link_is_not_rendered_as_a_link()
    {
        var rendered = CardFormatter.Format(Card(company: "[Acme](http://evil)"));

        rendered.ShouldContain(@"\[Acme\]\(http://evil\)");
    }

    [Fact]
    public void A_400_character_title_is_truncated_to_a_word_boundary()
    {
        var longTitle = string.Join(' ', Enumerable.Repeat("Engineer", 60));

        var rendered = CardFormatter.Format(Card(title: longTitle));

        rendered.ShouldContain("…");
    }

    [Fact]
    public void Only_the_first_three_reasons_are_rendered()
    {
        var rendered = CardFormatter.Format(Card(reasons:
            ["first reason", "second reason", "third reason", "fourth reason"]));

        rendered.ShouldContain("first reason");
        rendered.ShouldContain("third reason");
        rendered.ShouldNotContain("fourth reason");
    }

    [Fact]
    public void A_reason_with_a_double_newline_is_collapsed_to_a_single_space()
    {
        var rendered = CardFormatter.Format(Card(reasons: ["7 yrs Kafka\n\nagainst a role naming it"]));

        // The reason's own double newline is gone; the single blank line between the header block and the
        // reasons is layout, not the reason.
        rendered.ShouldContain("• 7 yrs Kafka against a role naming it");
        rendered.ShouldNotContain("Kafka\n\nagainst");
    }

    [Fact]
    public void A_reason_containing_a_backtick_is_escaped()
    {
        var rendered = CardFormatter.Format(Card(reasons: ["uses `kubectl` daily"]));

        rendered.ShouldContain(@"uses \`kubectl\` daily");
    }

    [Fact]
    public void A_long_reason_is_capped_at_ninety_graphemes()
    {
        var longReason = string.Join(' ', Enumerable.Repeat("word", 40));

        var rendered = CardFormatter.Format(Card(reasons: [longReason]));

        rendered.ShouldContain("…");
    }

    [Fact]
    public void A_blank_reason_is_skipped_and_the_next_takes_its_place()
    {
        var rendered = CardFormatter.Format(Card(reasons: ["   ", "real reason"]));

        rendered.ShouldContain("• real reason");
    }

    [Fact]
    public void The_score_is_shown_as_a_whole_number()
    {
        var rendered = CardFormatter.Format(Card(score: 86.7m));

        rendered.ShouldContain("🎯 *87*");
    }

    [Fact]
    public void A_sub_thousand_salary_is_shown_verbatim_not_abbreviated()
    {
        var rendered = CardFormatter.Format(Card(salary: new CardSalary(800, 950, "USD", IsEstimate: false, null)));

        rendered.ShouldContain("800–950 USD");
    }

    [Fact]
    public void A_salary_without_a_currency_omits_the_currency_suffix()
    {
        var rendered = CardFormatter.Format(Card(salary: new CardSalary(150000, 190000, null, IsEstimate: false, null)));

        rendered.ShouldContain("💰 150–190k ·");
        rendered.ShouldNotContain("EUR");
    }

    [Fact]
    public void Format_rejects_a_null_card() =>
        Should.Throw<ArgumentNullException>(() => CardFormatter.Format(null!));

    private static CardView Card(
        string title = "Senior Platform Engineer",
        string company = "Stripe",
        string? stage = "Series-public",
        string location = "Dublin / Remote EMEA",
        CardSalary? salary = null,
        decimal score = 87m,
        IReadOnlyList<string>? reasons = null) =>
        new(
            title,
            company,
            stage,
            location,
            salary ?? new CardSalary(150000, 190000, "EUR", IsEstimate: false, null),
            score,
            reasons ??
            [
                "7 yrs Kafka against a role naming Kafka as core",
                "Contractor-friendly, B2B stated explicitly",
                "EMEA timezone, no overlap requirement",
            ]);
}
