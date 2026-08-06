using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Formatting;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Formatting;

/// <summary>
/// The rendering corpus (F5 test-plan §The rendering corpus). Every layout in the message contract has a
/// committed snapshot, so any change to a formatter is visible in a diff and reviewed on purpose rather than
/// by accident. The snapshots are the exact bytes the bot would send — the contract's layouts made
/// executable. A drift is a deliberate decision (update the snapshot) or a regression (fix the formatter),
/// never a surprise in production.
/// </summary>
public sealed class RenderingCorpusSnapshotTests
{
    private static readonly string SnapshotDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "rendering-corpus");

    [Theory]
    [InlineData("header-full", nameof(HeaderFull))]
    [InlineData("header-nothing-new", nameof(HeaderNothingNew))]
    [InlineData("header-partial", nameof(HeaderPartial))]
    [InlineData("header-budget-reached", nameof(HeaderBudgetReached))]
    [InlineData("header-one-card", nameof(HeaderOneCard))]
    [InlineData("header-nine-cards", nameof(HeaderNineCards))]
    [InlineData("header-ten-cards", nameof(HeaderTenCards))]
    [InlineData("header-no-salary-stat", nameof(HeaderNoSalaryStat))]
    [InlineData("header-no-suppression", nameof(HeaderNoSuppression))]
    [InlineData("header-no-opportunity", nameof(HeaderNoOpportunity))]
    [InlineData("card-published-salary", nameof(CardPublishedSalary))]
    [InlineData("card-estimated-salary", nameof(CardEstimatedSalary))]
    [InlineData("card-estimated-high-conf", nameof(CardEstimatedHighConf))]
    [InlineData("card-estimated-low-conf", nameof(CardEstimatedLowConf))]
    [InlineData("card-no-salary", nameof(CardNoSalary))]
    [InlineData("card-one-reason", nameof(CardOneReason))]
    [InlineData("card-two-reasons", nameof(CardTwoReasons))]
    [InlineData("card-unknown-stage", nameof(CardUnknownStage))]
    [InlineData("card-sub-thousand-salary", nameof(CardSubThousandSalary))]
    [InlineData("card-hostile-input", nameof(CardHostileInput))]
    [InlineData("footer-full", nameof(FooterFull))]
    [InlineData("footer-only-suppressed", nameof(FooterOnlySuppressed))]
    [InlineData("footer-only-carried-over", nameof(FooterOnlyCarriedOver))]
    [InlineData("footer-only-degraded", nameof(FooterOnlyDegraded))]
    [InlineData("footer-suppressed-and-degraded", nameof(FooterSuppressedAndDegraded))]
    public void The_rendered_layout_matches_its_recorded_snapshot(string name, string sampleName)
    {
        var rendered = Render(sampleName);

        var path = Path.Combine(SnapshotDir, name + ".snapshot.txt");

        // Bootstrap mode (UPDATE_SNAPSHOTS=1) writes a missing snapshot so it can be reviewed and committed;
        // it never overwrites an existing one, so a layout regression is always a failing diff, never a silent
        // rewrite. Normal runs compare against the committed bytes with CRLF normalised to LF.
        var normalised = rendered.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!File.Exists(path)
            && string.Equals(Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS"), "1", StringComparison.Ordinal))
        {
            File.WriteAllText(path, normalised);
        }

        var expected = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        normalised.ShouldBe(expected);
    }

    private static string Render(string sampleName) => sampleName switch
    {
        nameof(HeaderFull) => DigestHeaderFormatter.Format(HeaderFull()),
        nameof(HeaderNothingNew) => DigestHeaderFormatter.Format(HeaderNothingNew()),
        nameof(HeaderPartial) => DigestHeaderFormatter.Format(HeaderPartial()),
        nameof(HeaderBudgetReached) => DigestHeaderFormatter.Format(HeaderBudgetReached()),
        nameof(HeaderOneCard) => DigestHeaderFormatter.Format(HeaderOneCard()),
        nameof(HeaderNineCards) => DigestHeaderFormatter.Format(HeaderNineCards()),
        nameof(HeaderTenCards) => DigestHeaderFormatter.Format(HeaderTenCards()),
        nameof(HeaderNoSalaryStat) => DigestHeaderFormatter.Format(HeaderNoSalaryStat()),
        nameof(HeaderNoSuppression) => DigestHeaderFormatter.Format(HeaderNoSuppression()),
        nameof(HeaderNoOpportunity) => DigestHeaderFormatter.Format(HeaderNoOpportunity()),
        nameof(CardPublishedSalary) => CardFormatter.Format(CardPublishedSalary()),
        nameof(CardEstimatedSalary) => CardFormatter.Format(CardEstimatedSalary()),
        nameof(CardEstimatedHighConf) => CardFormatter.Format(CardEstimatedHighConf()),
        nameof(CardEstimatedLowConf) => CardFormatter.Format(CardEstimatedLowConf()),
        nameof(CardNoSalary) => CardFormatter.Format(CardNoSalary()),
        nameof(CardOneReason) => CardFormatter.Format(CardOneReason()),
        nameof(CardTwoReasons) => CardFormatter.Format(CardTwoReasons()),
        nameof(CardUnknownStage) => CardFormatter.Format(CardUnknownStage()),
        nameof(CardSubThousandSalary) => CardFormatter.Format(CardSubThousandSalary()),
        nameof(CardHostileInput) => CardFormatter.Format(CardHostileInput()),
        nameof(FooterFull) => DigestFooterFormatter.Format(FooterFull()) ?? string.Empty,
        nameof(FooterOnlySuppressed) => DigestFooterFormatter.Format(FooterOnlySuppressed()) ?? string.Empty,
        nameof(FooterOnlyCarriedOver) => DigestFooterFormatter.Format(FooterOnlyCarriedOver()) ?? string.Empty,
        nameof(FooterOnlyDegraded) => DigestFooterFormatter.Format(FooterOnlyDegraded()) ?? string.Empty,
        nameof(FooterSuppressedAndDegraded) => DigestFooterFormatter.Format(FooterSuppressedAndDegraded()) ?? string.Empty,
        _ => throw new ArgumentOutOfRangeException(nameof(sampleName), sampleName, "Unknown corpus sample."),
    };

    private static HeaderView HeaderFull() =>
        new(
            DigestMode.Full, 127, 9, 185, 0, 0, 9, 34,
            ["salary floor", "timezone"], 0,
            new HeaderOpportunity("Staff Backend Engineer", "Snowflake", 95m,
                ["Kafka", "Azure", "distributed systems"]));

    private static HeaderView HeaderNothingNew() =>
        new(DigestMode.NothingNew, 0, 0, null, 340, 0, 0, 0, [], 0, null);

    private static HeaderView HeaderPartial() =>
        new(DigestMode.Partial, 84, 5, null, 0, 0, 5, 0, [], 43, null);

    private static HeaderView HeaderBudgetReached() =>
        new(DigestMode.BudgetReached, 127, 3, null, 0, 3, 3, 0, [], 0, null);

    private static HeaderOpportunity SampleOpportunity() =>
        new("Staff Backend Engineer", "Snowflake", 95m, ["Kafka", "Azure", "distributed systems"]);

    // A full-mode header with a single card below — the "1 card" boundary from the test-plan's layout matrix.
    private static HeaderView HeaderOneCard() =>
        new(DigestMode.Full, 12, 1, 172, 0, 0, 1, 4, ["salary floor"], 0, SampleOpportunity());

    private static HeaderView HeaderNineCards() =>
        new(DigestMode.Full, 127, 9, 185, 0, 0, 9, 34, ["salary floor", "timezone"], 0, SampleOpportunity());

    // Ten cards is the cap; the header still fits in six lines.
    private static HeaderView HeaderTenCards() =>
        new(DigestMode.Full, 210, 10, 190, 0, 0, 10, 51, ["salary floor", "timezone"], 0, SampleOpportunity());

    // Fewer than three salaried jobs → the average is omitted rather than showing a misleading two-job mean.
    private static HeaderView HeaderNoSalaryStat() =>
        new(DigestMode.Full, 40, 3, null, 0, 0, 3, 6, ["timezone"], 0, SampleOpportunity());

    // Nothing suppressed → the hidden clause disappears from the closing line.
    private static HeaderView HeaderNoSuppression() =>
        new(DigestMode.Full, 9, 4, 165, 0, 0, 4, 0, [], 0, SampleOpportunity());

    // A full day with no single stand-out role → no trophy line, still a valid header.
    private static HeaderView HeaderNoOpportunity() =>
        new(DigestMode.Full, 60, 5, 150, 0, 0, 5, 12, ["salary floor"], 0, null);

    private static CardView CardPublishedSalary() =>
        new(
            "Senior Platform Engineer", "Stripe", "Series-public", "Dublin / Remote EMEA",
            new CardSalary(150000, 190000, "EUR", IsEstimate: false, null), 87m,
            [
                "7 yrs Kafka against a role naming Kafka as core",
                "Contractor-friendly, B2B stated explicitly",
                "EMEA timezone, no overlap requirement",
            ]);

    private static CardView CardEstimatedSalary() =>
        new(
            "Senior Platform Engineer", "Stripe", "Series-public", "Dublin / Remote EMEA",
            new CardSalary(150000, 190000, "EUR", IsEstimate: true, "med conf"), 87m,
            [
                "7 yrs Kafka against a role naming Kafka as core",
                "Contractor-friendly, B2B stated explicitly",
                "EMEA timezone, no overlap requirement",
            ]);

    private static CardView CardEstimatedHighConf() =>
        CardEstimatedSalary() with { Salary = new CardSalary(150000, 190000, "EUR", IsEstimate: true, "high conf") };

    private static CardView CardEstimatedLowConf() =>
        CardEstimatedSalary() with { Salary = new CardSalary(150000, 190000, "EUR", IsEstimate: true, "low conf") };

    private static CardView CardNoSalary() =>
        CardPublishedSalary() with { Salary = null };

    private static CardView CardOneReason() =>
        CardPublishedSalary() with { Reasons = ["7 yrs Kafka against a role naming Kafka as core"] };

    private static CardView CardTwoReasons() =>
        CardPublishedSalary() with
        {
            Reasons =
            [
                "7 yrs Kafka against a role naming Kafka as core",
                "Contractor-friendly, B2B stated explicitly",
            ],
        };

    // An Unknown stage is dropped from the company line, never printed as the literal "Unknown".
    private static CardView CardUnknownStage() =>
        CardPublishedSalary() with { Stage = "Unknown" };

    // A sub-thousand stipend is shown verbatim, not rounded to "0k", so it is never misread as huge.
    private static CardView CardSubThousandSalary() =>
        CardPublishedSalary() with { Salary = new CardSalary(400, 900, "USD", IsEstimate: false, null) };

    private static CardView CardHostileInput() =>
        new(
            "*bold* [link](http://evil)", "[Acme](http://evil)", "Series-A", "Remote",
            new CardSalary(100000, 140000, "USD", IsEstimate: false, null), 73m,
            [
                "uses `kubectl` and _italics_ daily",
                "reason with\n\na double newline",
                "a+b=c, curly {braces} and a #hash",
            ]);

    private static FooterView FooterFull() =>
        new(
            34,
            [
                new FooterTally(21, "below salary floor"),
                new FooterTally(9, "timezone"),
                new FooterTally(4, "employment type"),
            ],
            2,
            ["greenhouse"]);

    // Only jobs hidden — no carry-over, no degraded source; lines two and three are omitted.
    private static FooterView FooterOnlySuppressed() =>
        new(9, [new FooterTally(9, "below salary floor")], 0, []);

    // Only jobs carried over to tomorrow — the hidden and degraded lines are absent.
    private static FooterView FooterOnlyCarriedOver() =>
        new(0, [], 5, []);

    // Only a degraded source — the quarantine warning stands alone.
    private static FooterView FooterOnlyDegraded() =>
        new(0, [], 0, ["greenhouse"]);

    private static FooterView FooterSuppressedAndDegraded() =>
        new(12, [new FooterTally(8, "below salary floor"), new FooterTally(4, "timezone")], 0, ["lever"]);
}
