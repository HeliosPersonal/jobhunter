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
/// <c>/company &lt;name&gt;</c> (F8 T09, SAD §6.2, AC-05): the Owner asks about a company by name and gets one
/// of three honest answers — a fresh dossier presented with its age, an acknowledgement that research was
/// queued for the next cycle when the dossier is stale or absent, or an offer to add the company when it is
/// not in the registry. It resolves through <see cref="ICompanyResearchQuery"/> and queues through
/// <see cref="IResearchRequestWriter"/>; freshness is judged against the injected <see cref="IClock"/> so the
/// stale/fresh boundary is deterministic. It never runs an LLM and never touches the CV (the CV crosses
/// exactly one boundary, not this one).
/// </summary>
public sealed class CompanyCommandHandlerTests
{
    private const long OwnerChat = 4242;
    private static readonly Guid Acme = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly ICompanyResearchQuery _research = Substitute.For<ICompanyResearchQuery>();
    private readonly IResearchRequestWriter _requests = Substitute.For<IResearchRequestWriter>();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 8, 6, 6, 0, 0, TimeSpan.Zero));

    private CompanyCommandHandler NewHandler() =>
        new(_research, _requests, _clock, NullLogger<CompanyCommandHandler>.Instance);

    private static ResearchClaimFacts Claim(
        ResearchCategory category, string text, string url, DateTimeOffset observedAt, bool isWarning = false) =>
        new(category, text, observedAt, url, isWarning);

    private static ResearchDossierSnapshot Dossier(
        DateTimeOffset generatedAt,
        IReadOnlyList<ResearchClaimFacts>? claims = null,
        IReadOnlyList<ResearchCategory>? unavailable = null,
        string summary = "A short honest summary.") =>
        new(summary, generatedAt, claims ?? [], unavailable ?? []);

    private void ResolvesTo(CompanyResearchLookup? lookup) =>
        _research.ResolveByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(lookup);

    [Fact]
    public async Task It_asks_for_a_name_when_the_command_stood_alone()
    {
        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages.ShouldHaveSingleItem().Text.ShouldContain("company", Case.Insensitive);
        await _research.DidNotReceive().ResolveByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _requests.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_company_offers_to_add_it_rather_than_failing()
    {
        ResolvesTo(null);

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "Nowhere Inc"));

        // AC / SAD §6.2: not in the registry — offer to add it, and queue nothing.
        messages.ShouldHaveSingleItem().Text.ShouldContain("Nowhere Inc");
        messages[0].Text.ShouldContain("registry", Case.Insensitive);
        await _requests.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_fresh_dossier_is_presented_with_its_age_and_is_not_re_queued()
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
        await _requests.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_known_company_never_researched_queues_and_acknowledges()
    {
        ResolvesTo(new CompanyResearchLookup(Acme, "Acme AI", LatestDossier: null));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "Acme AI"));

        // SAD §6.2 / AC-05: absent dossier — queue for the next cycle and acknowledge.
        messages.ShouldHaveSingleItem().Text.ShouldContain("Acme AI");
        messages[0].Text.ShouldContain("digest", Case.Insensitive);
        await _requests.Received(1).EnqueueAsync(Acme, Arg.Is<string>(r => !string.IsNullOrWhiteSpace(r)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_stale_dossier_queues_a_refresh_and_acknowledges()
    {
        // Older than the 30-day default window, so it is stale for every category.
        var stale = _clock.UtcNow.AddDays(-40);
        ResolvesTo(new CompanyResearchLookup(Acme, "Acme AI", Dossier(
            stale,
            claims: [Claim(ResearchCategory.Funding, "Raised a Series A.", "https://acme.ai/old", stale)])));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "Acme AI"));

        await _requests.Received(1).EnqueueAsync(Acme, Arg.Any<string>(), Arg.Any<CancellationToken>());
        string.Join("\n", messages.Select(m => m.Text)).ShouldContain("Acme AI");
    }

    [Fact]
    public async Task A_dossier_stale_only_for_a_volatile_category_is_refreshed()
    {
        // 10 days old: fresh for the 30-day default, but stale for News (7-day volatile window).
        var generated = _clock.UtcNow.AddDays(-10);
        ResolvesTo(new CompanyResearchLookup(Acme, "Acme AI", Dossier(
            generated,
            claims: [Claim(ResearchCategory.News, "Shipped a product.", "https://acme.ai/news", generated)])));

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "Acme AI"));

        await _requests.Received(1).EnqueueAsync(Acme, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_hostile_company_name_is_escaped_in_the_reply()
    {
        ResolvesTo(null);

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
        Should.Throw<ArgumentNullException>(() => new CompanyCommandHandler(null!, _requests, _clock, NullLogger<CompanyCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new CompanyCommandHandler(_research, null!, _clock, NullLogger<CompanyCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new CompanyCommandHandler(_research, _requests, null!, NullLogger<CompanyCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new CompanyCommandHandler(_research, _requests, _clock, null!));
    }
}
