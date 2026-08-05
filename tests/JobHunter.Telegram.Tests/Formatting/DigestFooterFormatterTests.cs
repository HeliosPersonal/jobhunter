using JobHunter.Telegram.Formatting;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Formatting;

/// <summary>
/// The footer (F5 message contract §Footer). It renders only when it has something to say, and each line is
/// omitted when its own count is zero. The hidden breakdown is what makes D7 and invariant 11 visible — a
/// suppressed job is accounted for under a stated reason, never lost.
/// </summary>
public sealed class DigestFooterFormatterTests
{
    [Fact]
    public void A_full_footer_renders_all_three_lines()
    {
        var rendered = DigestFooterFormatter.Format(Full());

        rendered.ShouldNotBeNull();
        rendered.ShouldContain("34 hidden: 21 below salary floor · 9 timezone · 4 employment type");
        rendered.ShouldContain("2 jobs still processing");
        rendered.ShouldContain("greenhouse");
        rendered.ShouldContain(@"\(quarantined\)");
    }

    [Fact]
    public void An_empty_footer_renders_nothing_so_the_digest_ends_on_the_last_card()
    {
        var footer = new FooterView(0, [], 0, []);

        DigestFooterFormatter.Format(footer).ShouldBeNull();
    }

    [Fact]
    public void The_still_processing_line_is_omitted_when_zero()
    {
        var footer = Full() with { StillProcessingCount = 0 };

        var rendered = DigestFooterFormatter.Format(footer);

        rendered.ShouldNotBeNull();
        rendered.ShouldNotContain("still processing");
    }

    [Fact]
    public void The_degraded_source_line_is_omitted_when_there_are_none()
    {
        var footer = Full() with { DegradedSources = [] };

        var rendered = DigestFooterFormatter.Format(footer);

        rendered.ShouldNotBeNull();
        rendered.ShouldNotContain("degraded");
    }

    [Fact]
    public void The_hidden_line_is_omitted_when_nothing_was_hidden()
    {
        var footer = new FooterView(0, [], StillProcessingCount: 2, DegradedSources: []);

        var rendered = DigestFooterFormatter.Format(footer);

        rendered.ShouldNotBeNull();
        rendered.ShouldNotContain("hidden");
        rendered.ShouldContain("2 jobs still processing");
    }

    [Fact]
    public void A_hostile_source_name_is_escaped()
    {
        var footer = new FooterView(0, [], 0, ["ev*il_source"]);

        var rendered = DigestFooterFormatter.Format(footer);

        rendered.ShouldNotBeNull();
        rendered.ShouldContain(@"ev\*il\_source");
    }

    [Fact]
    public void Each_degraded_source_gets_its_own_warning_line()
    {
        var footer = new FooterView(0, [], 0, ["greenhouse", "lever"]);

        var rendered = DigestFooterFormatter.Format(footer);

        rendered.ShouldNotBeNull();
        rendered!.Split('\n').Count(l => l.Contains("⚠️")).ShouldBe(2);
    }

    [Fact]
    public void Format_rejects_a_null_footer() =>
        Should.Throw<ArgumentNullException>(() => DigestFooterFormatter.Format(null!));

    private static FooterView Full() =>
        new(
            HiddenCount: 34,
            HiddenBreakdown:
            [
                new FooterTally(21, "below salary floor"),
                new FooterTally(9, "timezone"),
                new FooterTally(4, "employment type"),
            ],
            StillProcessingCount: 2,
            DegradedSources: ["greenhouse"]);
}
