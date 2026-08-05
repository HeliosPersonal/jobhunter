using JobHunter.Telegram.Formatting;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Formatting;

/// <summary>
/// The header — the three-second message (F5 message contract §Header, §Degraded-day variants). The
/// load-bearing rule is AC-01: at most six content lines in <em>every</em> variant. Each degraded day still
/// renders a header (never a missing message, ADR-F5-0001), the best opportunity is promoted above the
/// fold, and the hidden count is in the header where D7 is made visible.
/// </summary>
public sealed class DigestHeaderFormatterTests
{
    [Fact]
    public void A_full_header_states_counts_the_best_opportunity_and_the_hidden_line()
    {
        var rendered = DigestHeaderFormatter.Format(Full());

        rendered.ShouldContain("🌅 *Good morning\\.*");
        rendered.ShouldContain("*127* new · *9* strong matches · avg *185k USD*");
        rendered.ShouldContain("🏆 *Staff Backend Engineer* — Snowflake · *95*");
        rendered.ShouldContain("Kafka · Azure · distributed systems");
        rendered.ShouldContain("9 cards below");
        rendered.ShouldContain("34 hidden");
        rendered.ShouldContain("salary floor, timezone");
    }

    [Theory]
    [MemberData(nameof(AllVariants))]
    public void Every_variant_is_six_content_lines_or_fewer(HeaderView header)
    {
        var rendered = DigestHeaderFormatter.Format(header);

        var contentLines = rendered.Split('\n').Count(line => line.Trim().Length > 0);
        contentLines.ShouldBeLessThanOrEqualTo(DigestHeaderFormatter.MaxContentLines);
    }

    [Fact]
    public void The_nothing_new_variant_explains_itself_so_it_is_not_mistaken_for_broken()
    {
        var rendered = DigestHeaderFormatter.Format(NothingNew());

        rendered.ShouldContain("No new roles today");
        rendered.ShouldContain("340 companies checked");
        rendered.ShouldContain("This is normal");
        rendered.ShouldContain("Everything is working");
    }

    [Fact]
    public void The_partial_variant_is_labelled_and_states_what_will_appear_tomorrow()
    {
        var rendered = DigestHeaderFormatter.Format(Partial());

        rendered.ShouldContain(@"\(partial\)");
        rendered.ShouldContain("*84* new · *5* strong matches");
        rendered.ShouldContain("43 roles are still being analysed");
        rendered.ShouldContain("tomorrow");
    }

    [Fact]
    public void The_budget_reached_variant_is_labelled_reduced_and_reassures_nothing_was_lost()
    {
        var rendered = DigestHeaderFormatter.Format(BudgetReached());

        rendered.ShouldContain(@"\(reduced\)");
        rendered.ShouldContain("*127* new · *3* analysed before the daily budget was reached");
        rendered.ShouldContain("Nothing was lost");
    }

    [Fact]
    public void A_missing_average_salary_is_omitted_rather_than_shown_as_zero()
    {
        var rendered = DigestHeaderFormatter.Format(Full() with { AvgSalaryUsdThousands = null });

        rendered.ShouldContain("*127* new · *9* strong matches");
        rendered.ShouldNotContain("avg");
        rendered.ShouldNotContain("0k USD");
    }

    [Fact]
    public void A_full_header_with_no_opportunity_still_renders_the_counts_and_hidden_line()
    {
        var rendered = DigestHeaderFormatter.Format(Full() with { TopOpportunity = null });

        rendered.ShouldNotContain("🏆");
        rendered.ShouldContain("*127* new");
        rendered.ShouldContain("cards below");
    }

    [Fact]
    public void A_full_header_with_nothing_hidden_omits_the_hidden_clause()
    {
        var rendered = DigestHeaderFormatter.Format(
            Full() with { HiddenCount = 0, HiddenReasons = [] });

        rendered.ShouldContain("9 cards below");
        rendered.ShouldNotContain("hidden");
    }

    [Fact]
    public void A_hostile_company_name_in_the_opportunity_is_escaped()
    {
        var header = Full() with
        {
            TopOpportunity = new HeaderOpportunity("Engineer", "[Acme](http://evil)", 90m, ["Go"]),
        };

        var rendered = DigestHeaderFormatter.Format(header);

        rendered.ShouldContain(@"\[Acme\]\(http://evil\)");
    }

    [Fact]
    public void Format_rejects_a_null_header() =>
        Should.Throw<ArgumentNullException>(() => DigestHeaderFormatter.Format(null!));

    public static TheoryData<HeaderView> AllVariants()
    {
        var data = new TheoryData<HeaderView>();
        data.Add(Full());
        data.Add(NothingNew());
        data.Add(Partial());
        data.Add(BudgetReached());
        data.Add(Full() with { TopOpportunity = null });
        return data;
    }

    private static HeaderView Full() =>
        new(
            DigestMode.Full,
            TotalNewJobs: 127,
            StrongMatches: 9,
            AvgSalaryUsdThousands: 185,
            CompaniesChecked: 0,
            AnalysedCount: 0,
            CardCount: 9,
            HiddenCount: 34,
            HiddenReasons: ["salary floor", "timezone"],
            StillAnalysing: 0,
            TopOpportunity: new HeaderOpportunity(
                "Staff Backend Engineer", "Snowflake", 95m, ["Kafka", "Azure", "distributed systems"]));

    private static HeaderView NothingNew() =>
        new(
            DigestMode.NothingNew,
            TotalNewJobs: 0,
            StrongMatches: 0,
            AvgSalaryUsdThousands: null,
            CompaniesChecked: 340,
            AnalysedCount: 0,
            CardCount: 0,
            HiddenCount: 0,
            HiddenReasons: [],
            StillAnalysing: 0,
            TopOpportunity: null);

    private static HeaderView Partial() =>
        new(
            DigestMode.Partial,
            TotalNewJobs: 84,
            StrongMatches: 5,
            AvgSalaryUsdThousands: null,
            CompaniesChecked: 0,
            AnalysedCount: 0,
            CardCount: 5,
            HiddenCount: 0,
            HiddenReasons: [],
            StillAnalysing: 43,
            TopOpportunity: null);

    private static HeaderView BudgetReached() =>
        new(
            DigestMode.BudgetReached,
            TotalNewJobs: 127,
            StrongMatches: 3,
            AvgSalaryUsdThousands: null,
            CompaniesChecked: 0,
            AnalysedCount: 3,
            CardCount: 3,
            HiddenCount: 0,
            HiddenReasons: [],
            StillAnalysing: 0,
            TopOpportunity: null);
}
