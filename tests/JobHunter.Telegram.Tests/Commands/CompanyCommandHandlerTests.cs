using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using JobHunter.Domain.Research;
using JobHunter.Telegram.Commands;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// <c>/company &lt;name-or-domain&gt;</c> (catalogue §Company, AC-11): a <strong>read-only</strong> lookup.
/// Resolution is forgiving — a name, a domain and a bare label all resolve through
/// <see cref="ICompanyResearchQuery.ResolveCandidatesAsync"/>. A fresh dossier is presented with its age; a
/// known company whose dossier is stale or absent is offered <c>/research</c> rather than queueing a write
/// here (the queue is <c>/research</c>'s job, catalogue §Company · State ✎); an unknown company is offered as
/// an addition to the registry, never an empty result; and an ambiguous query offers every match so the Owner
/// can pick. It runs no LLM, queues nothing, and never touches the CV (the CV crosses one boundary, not this).
/// </summary>
public sealed class CompanyCommandHandlerTests
{
    private const long OwnerChat = 4242;
    private static readonly Guid Acme = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AcmeIo = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly ICompanyResearchQuery _research = Substitute.For<ICompanyResearchQuery>();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 8, 6, 6, 0, 0, TimeSpan.Zero));

    private CompanyCommandHandler NewHandler() =>
        new(_research, _clock, NullLogger<CompanyCommandHandler>.Instance);

    private static ResearchClaimFacts Claim(
        ResearchCategory category, string text, string url, DateTimeOffset observedAt, bool isWarning = false) =>
        new(category, text, observedAt, url, isWarning);

    private static ResearchDossierSnapshot Dossier(
        DateTimeOffset generatedAt,
        IReadOnlyList<ResearchClaimFacts>? claims = null,
        IReadOnlyList<ResearchCategory>? unavailable = null,
        string summary = "A short honest summary.") =>
        new(summary, generatedAt, claims ?? [], unavailable ?? []);

    private void ResolvesTo(params CompanyResearchLookup[] lookups) =>
        _research.ResolveCandidatesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(lookups);

    [Fact]
    public async Task It_asks_for_a_name_when_the_command_stood_alone()
    {
        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages.ShouldHaveSingleItem().Text.ShouldContain("company", Case.Insensitive);
        await _research.DidNotReceive().ResolveCandidatesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_company_offers_to_add_it_rather_than_failing()
    {
        ResolvesTo();

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "Nowhere Inc"));

        // AC-11: not in the registry — offer to add it, never an empty result.
        messages.ShouldHaveSingleItem().Text.ShouldContain("Nowhere Inc");
        messages[0].Text.ShouldContain("registry", Case.Insensitive);
    }

    [Fact]
    public async Task A_fresh_dossier_is_presented_with_its_age()
    {
        var generated = _clock.UtcNow.AddDays(-3);
        ResolvesTo(new CompanyResearchLookup(Acme, "Acme AI", Dossier(
            generated,
            claims: [Claim(ResearchCategory.Funding, "Raised a Series B.", "https://acme.ai/press", generated)])));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "Acme AI"));

        var text = string.Join("\n", messages.Select(m => m.Text));
        text.ShouldContain("Acme AI");
        text.ShouldContain("Raised a Series B");
        text.ShouldContain("(https://acme.ai/press)");
    }

    [Fact]
    public async Task A_known_company_never_researched_offers_research_rather_than_queueing()
    {
        ResolvesTo(new CompanyResearchLookup(Acme, "Acme AI", LatestDossier: null));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "Acme AI"));

        // Read-only: the absent dossier is answered with an offer to /research, not a silent queue write.
        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("Acme AI");
        text.ShouldContain("/research", Case.Insensitive);
    }

    [Fact]
    public async Task A_known_company_with_a_stale_dossier_shows_it_and_offers_a_refresh()
    {
        // Older than the 30-day default window, so it is stale for every category.
        var stale = _clock.UtcNow.AddDays(-40);
        ResolvesTo(new CompanyResearchLookup(Acme, "Acme AI", Dossier(
            stale,
            claims: [Claim(ResearchCategory.Funding, "Raised a Series A.", "https://acme.ai/old", stale)])));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "Acme AI"));

        var text = string.Join("\n", messages.Select(m => m.Text));
        text.ShouldContain("Acme AI");
        // The stale dossier is still shown (its claims are real), and a refresh is offered via /research.
        text.ShouldContain("Raised a Series A");
        text.ShouldContain("/research", Case.Insensitive);
    }

    [Fact]
    public async Task An_ambiguous_query_offers_every_match()
    {
        ResolvesTo(
            new CompanyResearchLookup(AcmeIo, "Acme Cloud", LatestDossier: null),
            new CompanyResearchLookup(Acme, "Acme Labs", LatestDossier: null));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "acme"));

        // A genuine ambiguity is surfaced so the Owner can pick, never silently resolved to the first.
        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("Acme Cloud");
        text.ShouldContain("Acme Labs");
    }

    [Fact]
    public async Task A_hostile_company_name_is_escaped_in_the_reply()
    {
        ResolvesTo();

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "Ev*il* Corp."));

        // The name is echoed through the escaper, so its markup cannot break the send.
        messages[0].Text.ShouldContain(@"Ev\*il\* Corp\.");
        messages[0].Text.ShouldNotContain("Ev*il*");
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => NewHandler().HandleAsync(null!));
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        Should.Throw<ArgumentNullException>(() => new CompanyCommandHandler(null!, _clock, NullLogger<CompanyCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new CompanyCommandHandler(_research, null!, NullLogger<CompanyCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new CompanyCommandHandler(_research, _clock, null!));
    }
}
