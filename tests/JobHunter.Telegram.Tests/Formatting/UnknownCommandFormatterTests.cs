using JobHunter.Application.Commands;
using JobHunter.Domain.Commands;
using JobHunter.Telegram.Formatting;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Formatting;

/// <summary>
/// The unknown-command reply (AC-09, ADR-F10-0002, contract §Unknown commands). A mistyped token within two
/// edits of a command names the nearest one; anything further off gets the grouped list instead of a guess.
/// Never an LLM. The mistyped token is shown in a code span so Telegram does not turn the typo itself into a
/// tappable command, while the suggestion is a plain <c>/command</c> the Owner can tap to re-send.
/// </summary>
public sealed class UnknownCommandFormatterTests
{
    private static readonly IReadOnlyList<CommandDescriptor> Commands =
    [
        new("pipeline", "Applications by status.", [],
            CommandCapability.Standard, CommandGroup.Pipeline, false, "/pipeline"),
        new("search", "Search live roles.", [],
            CommandCapability.Standard, CommandGroup.DigestAndDiscovery, false, "/search"),
    ];

    [Fact]
    public void A_near_typo_names_the_closest_command()
    {
        var text = UnknownCommandFormatter.Reply(Commands, "/pipline");

        text.ShouldContain("Did you mean");
        text.ShouldContain("/pipeline");
    }

    [Fact]
    public void The_mistyped_token_is_shown_but_not_as_a_tappable_command()
    {
        var text = UnknownCommandFormatter.Reply(Commands, "/pipline");

        // The typo sits inside a code span so Telegram does not linkify it; the suggestion is the tappable one.
        text.ShouldContain("`/pipline`");
    }

    [Fact]
    public void A_near_typo_points_the_owner_at_help_as_well()
    {
        var text = UnknownCommandFormatter.Reply(Commands, "/serch");

        text.ShouldContain("/search");
        text.ShouldContain("/help");
    }

    [Fact]
    public void A_token_far_from_every_command_gets_the_grouped_list_not_a_guess()
    {
        var text = UnknownCommandFormatter.Reply(Commands, "/frobnicate");

        text.ShouldNotContain("Did you mean");
        text.ShouldContain("*Pipeline*");
        text.ShouldContain("/pipeline");
        text.ShouldContain("/search");
    }

    [Fact]
    public void A_non_command_message_gets_the_grouped_list()
    {
        var text = UnknownCommandFormatter.Reply(Commands, "just chatting");

        text.ShouldContain("*Pipeline*");
    }

    [Fact]
    public void A_hostile_token_cannot_break_the_send()
    {
        var text = UnknownCommandFormatter.Reply(Commands, "/`*_[weird]");

        // No suggestion is close, so it falls back to the list; and nothing raw leaks that would fail the send.
        text.ShouldContain("*Pipeline*");
    }

    [Fact]
    public void A_null_command_list_is_rejected() =>
        Should.Throw<ArgumentNullException>(() => UnknownCommandFormatter.Reply(null!, "/x"));

    [Fact]
    public void A_null_token_is_rejected() =>
        Should.Throw<ArgumentNullException>(() => UnknownCommandFormatter.Reply(Commands, null!));
}
