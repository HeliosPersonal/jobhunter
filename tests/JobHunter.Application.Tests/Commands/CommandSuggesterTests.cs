using JobHunter.Application.Commands;
using JobHunter.Domain.Commands;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Commands;

/// <summary>
/// The unknown-command suggester (AC-09, ADR-F10-0002, contract §Unknown commands): a mistyped token is
/// matched by Damerau–Levenshtein against the registry names; within distance two it names the nearest
/// command, otherwise it declines so the caller can fall back to the grouped list. It is deterministic,
/// instant and free — never an LLM — and it is exercised against the real catalogue so every single-character
/// typo of a shipped command resolves to that command.
/// </summary>
public sealed class CommandSuggesterTests
{
    [Fact]
    public void A_one_character_deletion_suggests_the_intended_command()
    {
        var suggestion = CommandSuggester.Nearest(CommandCatalogue.Descriptors, "/pipline");

        suggestion.ShouldNotBeNull().Name.ShouldBe("pipeline");
    }

    [Fact]
    public void A_transposition_suggests_the_intended_command()
    {
        // Damerau's adjacent-transposition, not plain Levenshtein: "serach" → "search" is one edit.
        var suggestion = CommandSuggester.Nearest(CommandCatalogue.Descriptors, "serach");

        suggestion.ShouldNotBeNull().Name.ShouldBe("search");
    }

    [Theory]
    [InlineData("/pipline", "pipeline")]
    [InlineData("/serach", "search")]
    [InlineData("/cmpany", "company")]
    [InlineData("/statuss", "status")]
    [InlineData("/dgest", "digest")]
    [InlineData("/reserch", "research")]
    public void Every_misspelling_in_the_corpus_resolves_to_its_command(string typo, string expected)
    {
        var suggestion = CommandSuggester.Nearest(CommandCatalogue.Descriptors, typo);

        suggestion.ShouldNotBeNull().Name.ShouldBe(expected);
    }

    [Fact]
    public void A_leading_slash_is_optional_on_the_token()
    {
        CommandSuggester.Nearest(CommandCatalogue.Descriptors, "pipline")!.Name.ShouldBe("pipeline");
        CommandSuggester.Nearest(CommandCatalogue.Descriptors, "/pipline")!.Name.ShouldBe("pipeline");
    }

    [Fact]
    public void Matching_ignores_case()
    {
        CommandSuggester.Nearest(CommandCatalogue.Descriptors, "/PIPLINE")!.Name.ShouldBe("pipeline");
    }

    [Fact]
    public void A_token_more_than_two_edits_from_every_command_has_no_suggestion()
    {
        CommandSuggester.Nearest(CommandCatalogue.Descriptors, "frobnicate").ShouldBeNull();
    }

    [Fact]
    public void An_exact_command_name_is_its_own_nearest()
    {
        CommandSuggester.Nearest(CommandCatalogue.Descriptors, "/status")!.Name.ShouldBe("status");
    }

    [Fact]
    public void When_two_commands_tie_the_earlier_in_the_catalogue_wins()
    {
        var commands = new List<CommandDescriptor>
        {
            new("aa", "first", [], CommandCapability.Standard, CommandGroup.Meta, false, "/aa"),
            new("bb", "second", [], CommandCapability.Standard, CommandGroup.Meta, false, "/bb"),
        };

        // "xx" is distance two from both; the first listed is chosen for a stable, catalogue-ordered answer.
        CommandSuggester.Nearest(commands, "xx")!.Name.ShouldBe("aa");
    }

    [Fact]
    public void An_empty_token_has_no_suggestion()
    {
        CommandSuggester.Nearest(CommandCatalogue.Descriptors, "   ").ShouldBeNull();
    }

    [Fact]
    public void A_null_command_list_is_rejected() =>
        Should.Throw<ArgumentNullException>(() => CommandSuggester.Nearest(null!, "x"));

    [Fact]
    public void A_null_token_is_rejected() =>
        Should.Throw<ArgumentNullException>(() => CommandSuggester.Nearest(CommandCatalogue.Descriptors, null!));
}
