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
    [InlineData("card-published-salary", nameof(CardPublishedSalary))]
    [InlineData("card-estimated-salary", nameof(CardEstimatedSalary))]
    [InlineData("card-no-salary", nameof(CardNoSalary))]
    [InlineData("card-hostile-input", nameof(CardHostileInput))]
    [InlineData("footer-full", nameof(FooterFull))]
    public void The_rendered_layout_matches_its_recorded_snapshot(string name, string sampleName)
    {
        var rendered = Render(sampleName);

        var path = Path.Combine(SnapshotDir, name + ".snapshot.txt");
        var expected = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

        rendered.Replace("\r\n", "\n", StringComparison.Ordinal).ShouldBe(expected);
    }

    private static string Render(string sampleName) => sampleName switch
    {
        nameof(HeaderFull) => DigestHeaderFormatter.Format(HeaderFull()),
        nameof(HeaderNothingNew) => DigestHeaderFormatter.Format(HeaderNothingNew()),
        nameof(HeaderPartial) => DigestHeaderFormatter.Format(HeaderPartial()),
        nameof(HeaderBudgetReached) => DigestHeaderFormatter.Format(HeaderBudgetReached()),
        nameof(CardPublishedSalary) => CardFormatter.Format(CardPublishedSalary()),
        nameof(CardEstimatedSalary) => CardFormatter.Format(CardEstimatedSalary()),
        nameof(CardNoSalary) => CardFormatter.Format(CardNoSalary()),
        nameof(CardHostileInput) => CardFormatter.Format(CardHostileInput()),
        nameof(FooterFull) => DigestFooterFormatter.Format(FooterFull()) ?? string.Empty,
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

    private static CardView CardNoSalary() =>
        CardPublishedSalary() with { Salary = null };

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
}
