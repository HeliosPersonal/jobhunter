using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// <c>/digest</c> (contract §Commands, T11 AC): re-renders today's digest from stored state and returns the
/// messages, so the Owner can re-read the morning's cards on demand. It resolves the day's Run exactly as
/// delivery does (the live one, else the most recent), loads the persisted digest and renders it — but it
/// <strong>must not touch the delivery log and must not enter the delivery path</strong>: re-rendering and
/// re-delivering are different operations, and conflating them would re-send the morning's cards (AC).
/// </summary>
public sealed class DigestCommandHandlerTests
{
    private const long OwnerChat = 4242;
    private static readonly Guid RunId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly IDigestRepository _digests = Substitute.For<IDigestRepository>();
    private readonly IDigestRenderer _renderer = Substitute.For<IDigestRenderer>();

    private DigestCommandHandler NewHandler() =>
        new(_runs, _digests, _renderer, NullLogger<DigestCommandHandler>.Instance);

    [Fact]
    public async Task It_re_renders_the_days_digest_and_returns_the_rendered_messages()
    {
        var digest = SomeDigest();
        _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns(SomeRun());
        _digests.FindByRunAsync(RunId, Arg.Any<CancellationToken>()).Returns(digest);
        _renderer.RenderAsync(digest, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new RenderableMessage(CardKey.Header, RenderedMessage.PlainText("header")),
            new RenderableMessage(CardKey.Footer, RenderedMessage.PlainText("footer")),
        });

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages.Select(m => m.Text).ShouldBe(["header", "footer"]);
    }

    [Fact]
    public async Task It_falls_back_to_the_most_recent_run_when_no_run_is_active()
    {
        var digest = SomeDigest();
        _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns((Run?)null);
        _runs.FindMostRecentRunAsync(Arg.Any<CancellationToken>()).Returns(SomeRun());
        _digests.FindByRunAsync(RunId, Arg.Any<CancellationToken>()).Returns(digest);
        _renderer.RenderAsync(digest, Arg.Any<CancellationToken>())
            .Returns(new[] { new RenderableMessage(CardKey.Header, RenderedMessage.PlainText("h")) });

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task It_does_not_depend_on_the_delivery_log_at_all()
    {
        // Re-render is not re-deliver (AC): the handler's whole point is that it never enters the delivery path,
        // so it takes no IDeliveryLog dependency. A ctor that cannot be satisfied without one would fail here —
        // this asserts the type stays render-only, which is stronger than a DidNotReceive on an unused double.
        typeof(DigestCommandHandler).GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ShouldNotContain(typeof(IDeliveryLog));
    }

    [Fact]
    public async Task No_run_yields_a_plain_nothing_yet_message_and_renders_nothing()
    {
        _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns((Run?)null);
        _runs.FindMostRecentRunAsync(Arg.Any<CancellationToken>()).Returns((Run?)null);

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages.ShouldHaveSingleItem().Text.ShouldContain("No digest", Case.Insensitive);
        await _renderer.DidNotReceiveWithAnyArgs().RenderAsync(default!);
    }

    [Fact]
    public async Task A_run_without_a_persisted_digest_yields_a_plain_nothing_yet_message()
    {
        _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns(SomeRun());
        _digests.FindByRunAsync(RunId, Arg.Any<CancellationToken>()).Returns((Digest?)null);

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages.ShouldHaveSingleItem().Text.ShouldContain("No digest", Case.Insensitive);
        await _renderer.DidNotReceiveWithAnyArgs().RenderAsync(default!);
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => NewHandler().HandleAsync(null!));
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        Should.Throw<ArgumentNullException>(() => new DigestCommandHandler(null!, _digests, _renderer, NullLogger<DigestCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new DigestCommandHandler(_runs, null!, _renderer, NullLogger<DigestCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new DigestCommandHandler(_runs, _digests, null!, NullLogger<DigestCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new DigestCommandHandler(_runs, _digests, _renderer, null!));
    }

    private static readonly DateTimeOffset Now = new(2026, 8, 6, 2, 0, 0, TimeSpan.Zero);

    private static Run SomeRun() => new(RunId, Now.AddDays(-1), Now, ceilingUsd: 5m, Now);

    private static Digest SomeDigest() =>
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"), RunId, DigestMode.Full, totalNewJobs: 0,
            strongMatches: 0, avgSalaryUsd: null, suppressedCount: 0, suppressionBreakdown: [], carriedOverCount: 0,
            companiesChecked: 0, analysedCount: 0, degradedSources: [], narrative: null, NarrativeSource.Template,
            promptVersion: null, cards: [], generatedAt: Now);
}
