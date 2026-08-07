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
/// <c>/research &lt;name-or-domain&gt;</c> (catalogue §Company · State ✎, F8 AC-05): the command that
/// <strong>owns the on-demand research queue write</strong>. Resolution is the same forgiving lookup
/// <c>/company</c> uses, so a name, a domain and a bare label all match; an unknown company is offered as a
/// registry addition and an ambiguous query offers every match, never a silent write to the first. When a
/// company resolves, a dossier that is absent or stale is queued for the next cycle; a dossier that is still
/// fresh is <em>not</em> re-queued — its freshness is reported so a needless refresh is visible before it is
/// paid for. Research is batched and cost-ceilinged, so the confirmation is always "with tomorrow's digest",
/// never an inline result. No LLM, no CV (the CV crosses one boundary, not this).
/// </summary>
public sealed class ResearchCommandHandlerTests
{
    private const long OwnerChat = 4242;
    private static readonly Guid Acme = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AcmeIo = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly ICompanyResearchQuery _research = Substitute.For<ICompanyResearchQuery>();
    private readonly IResearchRequestWriter _requests = Substitute.For<IResearchRequestWriter>();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 8, 6, 6, 0, 0, TimeSpan.Zero));

    private ResearchCommandHandler NewHandler() =>
        new(_research, _requests, _clock, NullLogger<ResearchCommandHandler>.Instance);

    private static ResearchClaimFacts Claim(
        ResearchCategory category, string text, string url, DateTimeOffset observedAt, bool isWarning = false) =>
        new(category, text, observedAt, url, isWarning);

    private static ResearchDossierSnapshot Dossier(
        DateTimeOffset generatedAt,
        IReadOnlyList<ResearchClaimFacts>? claims = null,
        string summary = "A short honest summary.") =>
        new(summary, generatedAt, claims ?? [], []);

    private void ResolvesTo(params CompanyResearchLookup[] lookups) =>
        _research.ResolveCandidatesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(lookups);

    [Fact]
    public async Task It_asks_for_a_name_when_the_command_stood_alone()
    {
        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages.ShouldHaveSingleItem().Text.ShouldContain("company", Case.Insensitive);
        await _research.DidNotReceive().ResolveCandidatesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _requests.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_company_offers_to_add_it_and_queues_nothing()
    {
        ResolvesTo();

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "Nowhere Inc"));

        messages.ShouldHaveSingleItem().Text.ShouldContain("Nowhere Inc");
        messages[0].Text.ShouldContain("registry", Case.Insensitive);
        await _requests.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_ambiguous_query_offers_every_match_and_queues_nothing()
    {
        ResolvesTo(
            new CompanyResearchLookup(AcmeIo, "Acme Cloud", LatestDossier: null),
            new CompanyResearchLookup(Acme, "Acme Labs", LatestDossier: null));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "acme"));

        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("Acme Cloud");
        text.ShouldContain("Acme Labs");
        await _requests.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_company_never_researched_is_queued_for_tomorrows_digest()
    {
        ResolvesTo(new CompanyResearchLookup(Acme, "Acme AI", LatestDossier: null));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "Acme AI"));

        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("Acme AI");
        text.ShouldContain("digest", Case.Insensitive);
        await _requests.Received(1).EnqueueAsync(Acme, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_stale_dossier_is_queued_for_a_refresh()
    {
        // Older than the 30-day default window, so it is stale for every category.
        var stale = _clock.UtcNow.AddDays(-40);
        ResolvesTo(new CompanyResearchLookup(Acme, "Acme AI", Dossier(
            stale,
            claims: [Claim(ResearchCategory.Funding, "Raised a Series A.", "https://acme.ai/old", stale)])));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "Acme AI"));

        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("Acme AI");
        text.ShouldContain("digest", Case.Insensitive);
        await _requests.Received(1).EnqueueAsync(Acme, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_fresh_dossier_is_not_requeued_and_its_freshness_is_reported()
    {
        var fresh = _clock.UtcNow.AddDays(-3);
        ResolvesTo(new CompanyResearchLookup(Acme, "Acme AI", Dossier(
            fresh,
            claims: [Claim(ResearchCategory.Funding, "Raised a Series B.", "https://acme.ai/press", fresh)])));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "Acme AI"));

        // A needless refresh is made visible, not paid for: the freshness is reported and nothing is queued.
        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("Acme AI");
        text.ShouldContain("fresh", Case.Insensitive);
        await _requests.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_hostile_company_name_is_escaped_in_the_reply()
    {
        ResolvesTo();

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "Ev*il* Corp."));

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
        Should.Throw<ArgumentNullException>(() => new ResearchCommandHandler(null!, _requests, _clock, NullLogger<ResearchCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new ResearchCommandHandler(_research, null!, _clock, NullLogger<ResearchCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new ResearchCommandHandler(_research, _requests, null!, NullLogger<ResearchCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new ResearchCommandHandler(_research, _requests, _clock, null!));
    }
}
