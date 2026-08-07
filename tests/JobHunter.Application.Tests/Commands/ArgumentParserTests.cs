using JobHunter.Application.Commands;
using JobHunter.Domain.Commands;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Commands;

public sealed class ArgumentParserTests
{
    // The catalogue's documented /search filter vocabulary (command-catalogue.md §Argument parsing):
    // tech/stage/country are free text, min is a score (number), since is a duration, closed is a flag.
    private static readonly InlineFilterVocabulary Search = new(
    [
        new InlineFilterSpec("tech", InlineFilterKind.Text),
        new InlineFilterSpec("stage", InlineFilterKind.Text),
        new InlineFilterSpec("country", InlineFilterKind.Text),
        new InlineFilterSpec("min", InlineFilterKind.Number),
        new InlineFilterSpec("since", InlineFilterKind.Duration),
        new InlineFilterSpec("closed", InlineFilterKind.Boolean),
    ]);

    private static CommandDescriptor Company() =>
        new("company", "Company dossier.", [new ArgumentSpec("name", required: true, "Company name or domain.")],
            CommandCapability.Standard, CommandGroup.Meta, changesState: false, "/company");

    private static CommandDescriptor SearchCommand() =>
        new("search", "Search the corpus.", [new ArgumentSpec("query", required: false, "Free text with filters.")],
            CommandCapability.Standard, CommandGroup.Meta, changesState: false, "/search");

    private static CommandDescriptor NoArgs() =>
        new("digest", "Re-render today's digest.", [], CommandCapability.Standard, CommandGroup.Meta, changesState: false, "/digest");

    private static CommandDescriptor Floor() =>
        new("floor", "Set the salary floor.",
            [new ArgumentSpec("amount", required: true, "Amount."), new ArgumentSpec("currency", required: false, "ISO currency.")],
            CommandCapability.Standard, CommandGroup.Meta, changesState: true, "/floor", "Set your floor?");

    [Fact]
    public void Rejects_a_null_descriptor() =>
        Should.Throw<ArgumentNullException>(() => ArgumentParser.Parse("x", null!, Search));

    [Theory]
    [InlineData("stripe")]
    [InlineData("Stripe")]
    [InlineData("stripe.com")]
    public void Captures_a_company_argument_as_one_faithful_positional_token(string raw)
    {
        // Done-when #1: the parser must not fragment stripe.com nor misread Stripe as a filter — it hands
        // the token to the query verbatim, and ResolveByNameAsync/CanonicalDomain make the three equivalent.
        var result = ArgumentParser.Parse(raw, Company(), Search);

        result.Status.ShouldBe(ParseStatus.Complete);
        result.FreeText.ShouldBe(raw);
        result.Filters.ShouldBeEmpty();
    }

    [Fact]
    public void A_missing_required_argument_enters_the_multi_step_flow_rather_than_erroring()
    {
        var result = ArgumentParser.Parse(null, Company(), Search);

        result.Status.ShouldBe(ParseStatus.NeedsInput);
        result.MissingArgument!.Name.ShouldBe("name");
    }

    [Fact]
    public void A_blank_argument_string_also_enters_the_multi_step_flow()
    {
        var result = ArgumentParser.Parse("   ", Floor(), Search);

        result.Status.ShouldBe(ParseStatus.NeedsInput);
        result.MissingArgument!.Name.ShouldBe("amount");
    }

    [Fact]
    public void A_malformed_numeric_value_names_what_was_wrong_and_shows_the_usage_line()
    {
        var result = ArgumentParser.Parse("min:abc platform", SearchCommand(), Search);

        result.Status.ShouldBe(ParseStatus.Malformed);
        result.Problem.ShouldNotBeNull();
        result.Problem.ShouldContain("min");
        result.Problem.ShouldContain("abc");
        result.Usage.ShouldNotBeNull();
        result.Usage.ShouldStartWith("/search");
    }

    [Theory]
    [InlineData("closed:maybe")]
    [InlineData("since:soon")]
    public void A_malformed_flag_or_duration_value_is_reported_as_malformed(string token)
    {
        var result = ArgumentParser.Parse(token, SearchCommand(), Search);

        result.Status.ShouldBe(ParseStatus.Malformed);
    }

    [Fact]
    public void An_unknown_inline_filter_is_treated_as_search_text_with_a_note()
    {
        var result = ArgumentParser.Parse("wat:ever kafka", SearchCommand(), Search);

        result.Status.ShouldBe(ParseStatus.Complete);
        result.FreeText.ShouldContain("wat:ever");
        result.Notes.ShouldContain(n => n.Contains("wat", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_quoted_phrase_survives_as_a_single_term()
    {
        var result = ArgumentParser.Parse("\"staff engineer\" tech:go", SearchCommand(), Search);

        result.Status.ShouldBe(ParseStatus.Complete);
        result.FreeText.ShouldBe("staff engineer");
        result.Filters.ShouldContain(f => f.Key == "tech" && f.Value == "go");
    }

    [Fact]
    public void Duplicate_filters_are_deduplicated_case_insensitively()
    {
        var result = ArgumentParser.Parse("tech:go tech:Go tech:rust", SearchCommand(), Search);

        result.Filters.Where(f => f.Key == "tech").Select(f => f.Value)
            .ShouldBe(["go", "rust"]);
    }

    [Fact]
    public void No_parsed_value_reaches_a_query_as_raw_concatenated_text()
    {
        // Done-when #6: filters are lifted out into typed (key, value) pairs; the free text that remains
        // carries no filter syntax, so the query builder never receives "min:70 platform tech:go" as a blob.
        var result = ArgumentParser.Parse("min:70 platform tech:go", SearchCommand(), Search);

        result.Status.ShouldBe(ParseStatus.Complete);
        result.FreeText.ShouldBe("platform");
        result.FreeText.ShouldNotContain(":");
        result.Filters.ShouldContain(f => f.Key == "min" && f.Value == "70");
        result.Filters.ShouldContain(f => f.Key == "tech" && f.Value == "go");
    }

    [Fact]
    public void A_filter_key_is_matched_case_insensitively_and_normalised_to_lower_case()
    {
        var result = ArgumentParser.Parse("TECH:go", SearchCommand(), Search);

        result.Filters.ShouldContain(f => f.Key == "tech" && f.Value == "go");
    }

    [Fact]
    public void Accepts_valid_number_flag_and_duration_filters_together()
    {
        var result = ArgumentParser.Parse("min:70 closed:yes since:30d kafka", SearchCommand(), Search);

        result.Status.ShouldBe(ParseStatus.Complete);
        result.Filters.Select(f => f.Key).ShouldBe(["min", "closed", "since"]);
        result.FreeText.ShouldBe("kafka");
    }

    [Fact]
    public void Extra_arguments_to_a_no_argument_command_are_ignored_with_a_note()
    {
        var result = ArgumentParser.Parse("unexpected junk", NoArgs(), Search);

        result.Status.ShouldBe(ParseStatus.Complete);
        result.Notes.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_no_argument_command_with_no_input_is_complete_and_empty()
    {
        var result = ArgumentParser.Parse(null, NoArgs(), Search);

        result.Status.ShouldBe(ParseStatus.Complete);
        result.FreeText.ShouldBeEmpty();
        result.Filters.ShouldBeEmpty();
        result.Notes.ShouldBeEmpty();
    }

    [Fact]
    public void Without_a_vocabulary_every_colon_token_is_treated_as_free_text()
    {
        var result = ArgumentParser.Parse("tech:go kafka", SearchCommand(), InlineFilterVocabulary.None);

        result.Status.ShouldBe(ParseStatus.Complete);
        result.Filters.ShouldBeEmpty();
        result.FreeText.ShouldContain("tech:go");
    }

    [Fact]
    public void A_trailing_colon_token_is_plain_text_not_a_filter()
    {
        var result = ArgumentParser.Parse("tech: kafka", SearchCommand(), Search);

        result.Status.ShouldBe(ParseStatus.Complete);
        result.Filters.ShouldBeEmpty();
        result.FreeText.ShouldContain("tech:");
    }
}
