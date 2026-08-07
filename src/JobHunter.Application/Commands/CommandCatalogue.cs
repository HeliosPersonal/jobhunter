using JobHunter.Domain.Commands;

namespace JobHunter.Application.Commands;

/// <summary>
/// The one canonical list of every command (ADR-F10-0001, SAD §5) — the single source of truth the client
/// menu, the grouped <c>/help</c>, the <c>/start</c> list, the authorization check and the
/// catalogue-conformance suite all read. Nothing about the surface is hand-maintained anywhere else, so it
/// cannot drift: adding a command means adding a descriptor here <em>and</em> its section in
/// <c>contracts/command-catalogue.md</c>, or conformance stays red (test-plan §The catalogue-conformance
/// suite).
///
/// <para>The order is the catalogue's order, which is the order the grouped help and the menu present:
/// digest and discovery, pipeline, company, profile and preferences, operations, then meta. Each
/// descriptor carries its <see cref="CommandDescriptor.ContractAnchor"/> — the catalogue heading it maps
/// to — its <see cref="CommandCapability"/>, whether it <see cref="CommandDescriptor.ChangesState"/>, and,
/// for the state-changing ones, the confirmation prompt that names the effect before it happens (AC-07).</para>
/// </summary>
public static class CommandCatalogue
{
    /// <summary>The full command surface in catalogue order — the list the registry is built from.</summary>
    public static IReadOnlyList<CommandDescriptor> Descriptors { get; } =
    [
        // ---- Digest and discovery ----
        new("digest", "Re-read today's digest", [],
            CommandCapability.Standard, changesState: false, "/digest"),
        new("more", "The next cards below today's cut",
            [new ArgumentSpec("count", required: false, "How many cards to show (1–20, default 5).")],
            CommandCapability.Standard, changesState: false, "/more"),
        new("search", "Search live roles",
            [new ArgumentSpec("query", required: true, "Free text with optional key:value filters.")],
            CommandCapability.Standard, changesState: false, "/search"),
        new("hidden", "What today's ranking suppressed, by reason", [],
            CommandCapability.Standard, changesState: false, "/hidden"),

        // ---- Pipeline ----
        new("saved", "List the roles you saved", [],
            CommandCapability.Standard, changesState: false, "/saved"),
        new("pipeline", "Applications by status", [],
            CommandCapability.Standard, changesState: false, "/pipeline"),
        new("due", "Applications past their stage threshold", [],
            CommandCapability.Standard, changesState: false, "/due"),
        new("note", "Attach a note to your latest application",
            [new ArgumentSpec("text", required: false, "The note text; omitted to be asked for it.")],
            CommandCapability.Standard, changesState: true, "/note",
            confirmationPrompt: "Attach this note to your most recent application?"),
        new("stats", "This week's engagement", [],
            CommandCapability.Standard, changesState: false, "/stats"),

        // ---- Company ----
        new("company", "Look up a company by name or domain",
            [new ArgumentSpec("name-or-domain", required: true, "A company name or its canonical domain.")],
            CommandCapability.Standard, changesState: false, "/company"),
        new("research", "Request or refresh a company dossier",
            [new ArgumentSpec("name-or-domain", required: true, "A company name or its canonical domain.")],
            CommandCapability.Standard, changesState: true, "/research",
            confirmationPrompt: "Request a dossier for this company? It arrives with tomorrow's digest."),

        // ---- Profile and preferences ----
        new("cv", "Your active CV: version, activation date and match count", [],
            CommandCapability.Standard, changesState: false, "/cv"),
        new("prefs", "The preferences I've learned, each as one sentence", [],
            CommandCapability.Standard, changesState: false, "/prefs"),
        new("forget", "Switch off a learned preference by dimension",
            [new ArgumentSpec("dimension", required: false, "The dimension to forget; omitted to pick from a list.")],
            CommandCapability.Standard, changesState: true, "/forget",
            confirmationPrompt: "Switch off this learned preference? It takes effect on the next ranking."),
        new("floor", "Set your explicit salary floor (previewed before it's applied)",
            [
                new ArgumentSpec("amount", required: true, "The salary floor amount."),
                new ArgumentSpec("currency", required: false, "ISO currency; defaults to EUR."),
            ],
            CommandCapability.Standard, changesState: true, "/floor",
            confirmationPrompt: "Set this salary floor? It overrides any learned salary preference."),

        // ---- Operations ----
        new("status", "Last run's outcome, cost against ceiling, counts and degraded sources", [],
            CommandCapability.Sensitive, changesState: false, "/status"),
        new("cost", "This month's spend by stage and tier, flagging estimate-vs-actual drift",
            [new ArgumentSpec("month", required: false, "A YYYY-MM month; defaults to the current one.")],
            CommandCapability.Sensitive, changesState: false, "/cost"),
        new("sources", "Per-provider fetch health and quarantined sources", [],
            CommandCapability.Sensitive, changesState: false, "/sources"),
        new("run", "Trigger the daily pipeline off-schedule — refused when a run is live", [],
            CommandCapability.Sensitive, changesState: true, "/run",
            confirmationPrompt: "Start an off-schedule run now, under the configured cost ceiling?"),
        new("redeliver", "Re-send today's digest — states how many cards would actually be sent", [],
            CommandCapability.Sensitive, changesState: true, "/redeliver",
            confirmationPrompt: "Re-send today's digest? Only cards not already delivered will go out."),

        // ---- Meta ----
        new("start", "Confirm this chat is authorised", [],
            CommandCapability.Standard, changesState: false, "/start"),
        new("help", "Show this command list",
            [new ArgumentSpec("command", required: false, "A command name for its detailed usage.")],
            CommandCapability.Standard, changesState: false, "/help"),
        new("cancel", "Abandon any pending command or confirmation", [],
            CommandCapability.Standard, changesState: false, "/cancel"),
    ];
}
