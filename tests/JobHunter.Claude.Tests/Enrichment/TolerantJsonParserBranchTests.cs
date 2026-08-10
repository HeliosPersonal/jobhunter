using JobHunter.Claude.Enrichment;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Enrichment;

/// <summary>
/// The tolerant-enrichment parser's residual defensive arms, complementing the fixture-driven
/// <see cref="TolerantJsonParserTests"/>: a technologies array carrying a non-string element (silently skipped,
/// QG-3) and a currency whose length is not three characters (the <c>IsRealIso4217</c> length guard, which drops
/// the salary but keeps the rest of the assessment). Neither throws for a single item.
/// </summary>
public sealed class TolerantJsonParserBranchTests
{
    private const string Head =
        "\"isRemote\":true,\"isContractorFriendly\":false,\"reasons\":[\"fits the stack\"]";

    [Fact]
    public void A_non_string_element_in_the_technologies_array_is_skipped()
    {
        // ReadStringArray's non-string continue arm: the number and null are dropped, the strings survive.
        var outcome = TolerantJsonParser.Parse(
            $$"""{"technologies":["Go",7,null,"Rust"],{{Head}}}""");

        outcome.IsSuccess.ShouldBeTrue();
        outcome.Output!.Technologies.ShouldBe(["Go", "Rust"]);
    }

    [Fact]
    public void A_currency_that_is_not_three_characters_drops_the_salary_and_is_noted()
    {
        // IsRealIso4217's `code.Length != 3` arm: a two-letter code is well-formed JSON but not a currency, so
        // the salary is dropped (conservatively) while the rest of the assessment still parses.
        var outcome = TolerantJsonParser.Parse(
            $$"""{"salary":{"min":90000,"max":120000,"currency":"US","period":"Year"},{{Head}}}""");

        outcome.IsSuccess.ShouldBeTrue();
        outcome.Output!.Salary.ShouldBeNull();
        outcome.Anomalies.ShouldContain(a => a.Contains("unknown currency"));
    }
}
