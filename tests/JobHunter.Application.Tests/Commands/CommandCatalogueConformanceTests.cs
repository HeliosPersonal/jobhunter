using System.Text.RegularExpressions;
using JobHunter.Application.Commands;
using JobHunter.Domain.Commands;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Commands;

/// <summary>
/// The catalogue-conformance suite (test-plan §The catalogue-conformance suite) — the real deliverable of
/// F10 T10. The <see cref="CommandCatalogue"/> descriptor list and <c>contracts/command-catalogue.md</c>
/// are asserted to be a bijection in both directions, every command is asserted to declare a capability,
/// and every state-changing command is asserted to carry a confirmation path (SAD §10 QG-1, QG-2). Each of
/// the four assertions is paired with a deliberately non-compliant fixture proving it can go red — an
/// assertion that cannot fail guards nothing.
/// </summary>
public sealed class CommandCatalogueConformanceTests
{
    // The real surface: the canonical descriptor list, assembled into the registry exactly as production does.
    private static readonly CommandRegistry Registry = new(CommandCatalogue.Descriptors);

    // The command tokens documented in the catalogue, parsed from its H3 headings once.
    private static readonly IReadOnlySet<string> CatalogueAnchors = ParseCatalogueAnchors();

    // ---- Assertion 1: Registry → contract. Every descriptor anchor resolves to a catalogue heading. ----

    [Fact]
    public void Every_descriptor_anchor_resolves_to_a_catalogue_heading()
    {
        var unmatched = AnchorsWithoutHeading(Registry.Commands, CatalogueAnchors);

        unmatched.ShouldBeEmpty(
            $"These commands are built but not documented: {string.Join(", ", unmatched)}.");
    }

    [Fact]
    public void Registry_to_contract_goes_red_when_a_command_is_undocumented()
    {
        // A descriptor whose anchor has no heading is exactly the drift this direction catches.
        var withUndocumented = Registry.Commands
            .Append(new CommandDescriptor("ghost", "Built but never written down.", [],
                CommandCapability.Standard, changesState: false, "/ghost"))
            .ToList();

        AnchorsWithoutHeading(withUndocumented, CatalogueAnchors).ShouldBe(["/ghost"]);
    }

    // ---- Assertion 2: Contract → registry. Every catalogue heading has a descriptor. ----

    [Fact]
    public void Every_catalogue_heading_has_a_descriptor()
    {
        var unmatched = HeadingsWithoutDescriptor(Registry.Commands, CatalogueAnchors);

        unmatched.ShouldBeEmpty(
            $"These commands are documented but not built: {string.Join(", ", unmatched)}.");
    }

    [Fact]
    public void Contract_to_registry_goes_red_when_a_documented_command_is_unbuilt()
    {
        // Drop one built command; its still-documented heading must be reported as unbuilt — the direction
        // usually missed, and how a catalogue turns into fiction.
        var missingOne = Registry.Commands.Where(c => c.ContractAnchor != "/redeliver").ToList();

        HeadingsWithoutDescriptor(missingOne, CatalogueAnchors).ShouldBe(["/redeliver"]);
    }

    // ---- Assertion 3 (safety, capability): every command declares a capability. ----

    [Fact]
    public void Every_command_declares_a_capability() =>
        Registry.Commands.ShouldAllBe(c => c.Capability != CommandCapability.Unspecified);

    [Fact]
    public void Capability_declaration_goes_red_when_a_command_forgets_it() =>
        // The guard is at descriptor construction: a forgotten capability fails closed rather than
        // silently defaulting to an everyday command (QG-2).
        Should.Throw<ArgumentException>(() =>
            new CommandDescriptor("x", "s", [], CommandCapability.Unspecified, changesState: false, "/x"));

    // ---- Assertion 4 (safety, confirmation): every state-changing command has a confirmation path. ----

    [Fact]
    public void Every_state_changing_command_has_a_confirmation_path() =>
        Registry.Commands
            .Where(c => c.ChangesState)
            .ShouldAllBe(c => !string.IsNullOrWhiteSpace(c.ConfirmationPrompt));

    [Fact]
    public void Confirmation_path_goes_red_when_a_state_changing_command_omits_it() =>
        // The registry fails fast at startup, naming the offending command, so a state-changing command
        // with no confirmation path never serves traffic (QG-2).
        Should.Throw<InvalidOperationException>(() =>
            new CommandRegistry([
                new CommandDescriptor("mutate", "Changes state with no confirmation.", [],
                    CommandCapability.Sensitive, changesState: true, "/mutate", confirmationPrompt: null),
            ]));

    // ---- Conformance helpers (reflection over the registry + markdown parse). ----

    private static List<string> AnchorsWithoutHeading(
        IReadOnlyList<CommandDescriptor> commands, IReadOnlySet<string> headings) =>
        commands.Select(c => c.ContractAnchor)
            .Where(anchor => !headings.Contains(anchor))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(a => a, StringComparer.Ordinal)
            .ToList();

    private static List<string> HeadingsWithoutDescriptor(
        IReadOnlyList<CommandDescriptor> commands, IReadOnlySet<string> headings)
    {
        var anchors = commands.Select(c => c.ContractAnchor).ToHashSet(StringComparer.Ordinal);
        return headings
            .Where(heading => !anchors.Contains(heading))
            .OrderBy(h => h, StringComparer.Ordinal)
            .ToList();
    }

    private static HashSet<string> ParseCatalogueAnchors()
    {
        var path = Path.Combine(RepositoryRoot(),
            "docs", "features", "f10-telegram-commands", "contracts", "command-catalogue.md");
        var anchors = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines(path))
        {
            if (!line.StartsWith("### ", StringComparison.Ordinal))
            {
                continue;
            }

            // A heading is a backtick-quoted token with optional args, e.g. "### `/more [count]`".
            // The ContractAnchor is the leading /token; the args in the heading are documentation only.
            var match = Regex.Match(line, @"^###\s+`(/[A-Za-z]+)");
            if (match.Success)
            {
                anchors.Add(match.Groups[1].Value);
            }
        }

        return anchors;
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "JobHunter.slnx")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("Could not locate the repository root (JobHunter.slnx) from the test output directory.");
        return dir!.FullName;
    }
}
