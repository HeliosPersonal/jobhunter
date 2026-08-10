using JobHunter.Application.Commands;
using JobHunter.Telegram.Formatting;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Formatting;

/// <summary>
/// The command-surface corpus (T10 done-when: "every command has a committed rendering snapshot in the
/// shared F5 corpus"). Every command in the canonical <see cref="CommandCatalogue"/> has a committed
/// snapshot of its <c>/help [command]</c> usage, and the two surfaces the whole catalogue projects to — the
/// grouped <c>/help</c> list and the unknown-command reply — are snapshotted too. Because the per-command
/// theory is driven from the real descriptor list, a command added without a reviewed snapshot fails the
/// build, so the surface the Owner reads is the exact bytes the bot would send and cannot drift from the
/// registry (ADR-F10-0001, AC-09). The snapshots live beside the F5 rendering corpus, under the same
/// bootstrap-once, never-overwrite discipline.
/// </summary>
public sealed class CommandSurfaceSnapshotTests
{
    private static readonly string SnapshotDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "rendering-corpus");

    public static TheoryData<string> EveryCommand()
    {
        var data = new TheoryData<string>();
        foreach (var descriptor in CommandCatalogue.Descriptors)
        {
            data.Add(descriptor.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryCommand))]
    public void Each_commands_usage_matches_its_recorded_snapshot(string commandName)
    {
        var descriptor = CommandCatalogue.Descriptors.Single(d => d.Name == commandName);

        var rendered = HelpFormatter.Usage(HelpText.Usage(descriptor));

        AssertMatchesSnapshot("command-usage-" + commandName, rendered);
    }

    [Fact]
    public void The_grouped_help_list_matches_its_recorded_snapshot()
    {
        var rendered = HelpFormatter.GroupedList(HelpText.Grouped(CommandCatalogue.Descriptors));

        AssertMatchesSnapshot("help-grouped", rendered);
    }

    [Fact]
    public void The_start_greeting_matches_its_recorded_snapshot()
    {
        // /start is a fixed greeting followed by the same grouped list; the greeting frame is snapshotted here.
        var greeting = MarkdownV2Escaper.Escape("This chat (4242) is authorised. Here are the commands:");
        var list = HelpFormatter.GroupedList(HelpText.Grouped(CommandCatalogue.Descriptors));

        AssertMatchesSnapshot("start-greeting", greeting + "\n\n" + list);
    }

    [Fact]
    public void The_near_typo_unknown_reply_matches_its_recorded_snapshot()
    {
        var rendered = UnknownCommandFormatter.Reply(CommandCatalogue.Descriptors, "/pipline");

        AssertMatchesSnapshot("unknown-near-typo", rendered);
    }

    [Fact]
    public void The_far_token_unknown_reply_matches_its_recorded_snapshot()
    {
        var rendered = UnknownCommandFormatter.Reply(CommandCatalogue.Descriptors, "/frobnicate");

        AssertMatchesSnapshot("unknown-fallback-list", rendered);
    }

    private static void AssertMatchesSnapshot(string name, string rendered)
    {
        var path = Path.Combine(SnapshotDir, name + ".snapshot.txt");

        // Bootstrap mode (UPDATE_SNAPSHOTS=1) writes a missing snapshot so it can be reviewed and committed;
        // it never overwrites an existing one, so a surface regression is always a failing diff, never a
        // silent rewrite. Normal runs compare against the committed bytes with CRLF normalised to LF.
        var normalised = rendered.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!File.Exists(path)
            && string.Equals(Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS"), "1", StringComparison.Ordinal))
        {
            File.WriteAllText(path, normalised);
        }

        var expected = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        normalised.ShouldBe(expected);
    }
}
