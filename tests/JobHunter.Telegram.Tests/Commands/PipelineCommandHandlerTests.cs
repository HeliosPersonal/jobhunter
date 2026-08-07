using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using JobHunter.Telegram.Commands;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// <c>/pipeline</c> (contract §Commands, F6 T09 done-when 3): the tracked applications grouped by status, in
/// the same scannable card layout as the digest (AC-01). It reads the pipeline view through
/// <see cref="IApplicationPipelineQuery"/> — never <c>DateTime.Now</c>, so <c>daysInStage</c> is computed
/// against the caller's clock — and renders each application through the one shared card formatter, so there
/// is no second layout. A closed posting is a marker, never a status (AC-07). An empty pipeline is a plain,
/// helpful line, never an empty message; the CV is nowhere near it (the CV crosses exactly one boundary).
/// </summary>
public sealed class PipelineCommandHandlerTests
{
    private const long OwnerChat = 4242;

    private static readonly DateTimeOffset Now = new(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);

    private readonly IApplicationPipelineQuery _pipeline = Substitute.For<IApplicationPipelineQuery>();
    private readonly FakeClock _clock = new(Now);

    private PipelineCommandHandler NewHandler() =>
        new(_pipeline, _clock, NullLogger<PipelineCommandHandler>.Instance);

    private static PipelineEntry Entry(
        string title, string company, decimal score, bool postingClosed = false, int daysInStage = 3) => new(
        Guid.NewGuid(), Guid.NewGuid(), title, company, score, postingClosed,
        AppliedAt: null, LastActivityAt: Now.AddDays(-daysInStage), NextActionAt: null, daysInStage);

    private static ApplicationPipeline Pipeline(params PipelineGroup[] groups) => new(groups);

    [Fact]
    public async Task It_renders_one_card_message_per_application_grouped_by_status()
    {
        _pipeline.PipelineAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Pipeline(
                new PipelineGroup(ApplicationStatus.Interview, [Entry("Staff Backend Engineer", "Snowflake", 95m)]),
                new PipelineGroup(ApplicationStatus.Applied, [Entry("Senior SRE", "Acme", 88m)])));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        // The two jobs each appear in their own scannable card — the same layout as the digest.
        messages.ShouldContain(m => m.Text.Contains("Staff Backend Engineer") && m.Text.Contains("Snowflake") && m.Text.Contains("95"));
        messages.ShouldContain(m => m.Text.Contains("Senior SRE") && m.Text.Contains("Acme") && m.Text.Contains("88"));
    }

    [Fact]
    public async Task It_shows_a_status_header_for_each_group()
    {
        _pipeline.PipelineAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Pipeline(
                new PipelineGroup(ApplicationStatus.Interview, [Entry("Staff Backend Engineer", "Snowflake", 95m)]),
                new PipelineGroup(ApplicationStatus.Applied, [Entry("Senior SRE", "Acme", 88m)])));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages.ShouldContain(m => m.Text.Contains("Interview"));
        messages.ShouldContain(m => m.Text.Contains("Applied"));
    }

    [Fact]
    public async Task A_closed_posting_is_marked_without_a_status_change()
    {
        _pipeline.PipelineAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Pipeline(
                new PipelineGroup(ApplicationStatus.Saved, [Entry("Platform Engineer", "Vanished Inc", 70m, postingClosed: true)])));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages.ShouldContain(m => m.Text.Contains("Platform Engineer") && m.Text.Contains("closed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Each_entry_carries_buttons_for_its_legal_next_transitions_only()
    {
        _pipeline.PipelineAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Pipeline(
                new PipelineGroup(ApplicationStatus.Interview, [Entry("Staff Backend Engineer", "Snowflake", 95m)])));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        // Advancing costs one tap: an Interview entry offers exactly its legal next moves (Rejected, Offer,
        // Ignored per the F6 matrix, in funnel order), never a backwards or impossible one, and never a move
        // to where it already is (AC-03, catalogue §/pipeline).
        var card = messages.Single(m => m.Text.Contains("Staff Backend Engineer"));
        card.HasKeyboard.ShouldBeTrue();
        var labels = card.Keyboard.SelectMany(row => row).Select(b => b.Label).ToArray();
        labels.ShouldBe(["Rejected", "Offer", "Ignored"]);
        card.Keyboard.SelectMany(row => row).ShouldAllBe(b => b.CallbackData != null);
    }

    [Fact]
    public async Task A_terminal_status_entry_carries_only_its_real_moves()
    {
        _pipeline.PipelineAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Pipeline(
                new PipelineGroup(ApplicationStatus.Offer, [Entry("Senior SRE", "Acme", 88m)])));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        // An Offer is accepted (stays Offer) or declined (Rejected) — the only real move is Rejected; the
        // idempotent no-op is not offered as a button (AC-03).
        var card = messages.Single(m => m.Text.Contains("Senior SRE"));
        card.Keyboard.SelectMany(row => row).Select(b => b.Label).ShouldBe(["Rejected"]);
    }

    [Fact]
    public async Task An_empty_pipeline_yields_one_plain_helpful_line()
    {
        _pipeline.PipelineAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Pipeline());

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages.ShouldHaveSingleItem().Text.ShouldContain("pipeline", Case.Insensitive);
    }

    [Fact]
    public async Task It_reads_the_pipeline_as_of_the_clock()
    {
        _pipeline.PipelineAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Pipeline());

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        // daysInStage is computed against the caller's clock, never DateTime.Now (coding-standards §IClock).
        await _pipeline.Received(1).PipelineAsync(Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => NewHandler().HandleAsync(null!));
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        Should.Throw<ArgumentNullException>(() =>
            new PipelineCommandHandler(null!, _clock, NullLogger<PipelineCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() =>
            new PipelineCommandHandler(_pipeline, null!, NullLogger<PipelineCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() =>
            new PipelineCommandHandler(_pipeline, _clock, null!));
    }
}
