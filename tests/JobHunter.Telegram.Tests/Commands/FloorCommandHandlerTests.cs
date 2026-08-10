using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Commands;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Profiles;
using JobHunter.Telegram.Commands;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// <c>/floor &lt;amount&gt; [currency]</c> (catalogue §Profile, F10 T08): sets the Owner's explicit salary floor,
/// which outranks any learned salary weight (F4 AC-05). The change is <strong>previewed before it is made</strong>:
/// the reply states how many of today's shown jobs the floor would have affected, then stores a short-lived
/// per-chat confirm state and asks the Owner to confirm — nothing is written in this step (the confirm→write
/// resume is T10, exactly as <c>/note</c>'s reply resume is). Forgiving by construction: no amount lists the usage
/// rather than erroring; a malformed or non-positive amount is a business outcome with a usage line, never an
/// exception. The currency defaults to EUR and is upper-cased. Every value reaches the reply through the one
/// MarkdownV2 escaper. The CV is nowhere near it — it crosses exactly one boundary, and this is not it.
/// </summary>
public sealed class FloorCommandHandlerTests
{
    private const long OwnerChat = 4242;

    private static readonly DateTimeOffset Now = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly ISalaryFloorPreviewQuery _preview = Substitute.For<ISalaryFloorPreviewQuery>();
    private readonly IConversationStateStore _state = Substitute.For<IConversationStateStore>();
    private readonly IProfileRepository _profiles = Substitute.For<IProfileRepository>();
    private readonly FakeClock _clock = new(Now);

    private FloorCommandHandler NewHandler() => new(
        _preview, _state, _profiles, _clock, NullLogger<FloorCommandHandler>.Instance);

    private void PreviewReturns(int affected) =>
        _preview.CountAffectedAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(affected);

    private static Profile NewProfile() => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        isActive: true,
        displayName: "Owner",
        salaryFloor: null,
        salaryFloorCurrency: null,
        timezoneBand: TimezoneBand.EMEA,
        preferredCountries: ["Ukraine", "Poland"],
        employmentTypes: [EmploymentType.FullTime, EmploymentType.Contract],
        updatedAt: Now);

    private static CommandResumeRequest ConfirmResume(string amount, string currency) => new(
        OwnerChat,
        "confirm",
        new Dictionary<string, string> { ["amount"] = amount, ["currency"] = currency },
        "confirm");

    [Fact]
    public async Task No_amount_lists_the_usage_rather_than_erroring()
    {
        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages.ShouldHaveSingleItem().Text.ShouldContain("amount");
        await _state.DidNotReceive().SetAsync(Arg.Any<long>(), Arg.Any<ConversationState>(), Arg.Any<CancellationToken>());
        await _preview.DidNotReceive().CountAffectedAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_malformed_amount_is_a_business_outcome_with_a_usage_line()
    {
        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "not-a-number"));

        messages.ShouldHaveSingleItem();
        await _preview.DidNotReceive().CountAffectedAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _state.DidNotReceive().SetAsync(Arg.Any<long>(), Arg.Any<ConversationState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_non_positive_amount_is_rejected_without_a_preview()
    {
        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "0"));

        messages.ShouldHaveSingleItem();
        await _preview.DidNotReceive().CountAffectedAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_valid_amount_previews_the_affected_count_and_asks_to_confirm()
    {
        PreviewReturns(3);

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "120000"));

        // The preview runs in the default currency (EUR) at the parsed amount, and the reply states the count.
        await _preview.Received(1).CountAffectedAsync(120_000m, "EUR", Arg.Any<CancellationToken>());
        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("3");
        text.ShouldContain("120");
    }

    [Fact]
    public async Task A_valid_amount_stores_a_confirm_state_carrying_the_parsed_amount_and_currency()
    {
        PreviewReturns(0);

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "150000 usd"));

        // The preview uses the supplied currency, upper-cased; the pending state carries the parsed values so the
        // confirm step (T10) can write them — structured values, not free-text the Owner typed.
        await _preview.Received(1).CountAffectedAsync(150_000m, "USD", Arg.Any<CancellationToken>());
        await _state.Received(1).SetAsync(
            OwnerChat,
            Arg.Is<ConversationState>(s =>
                s != null
                && s.Command == "floor"
                && s.Context["amount"] == "150000"
                && s.Context["currency"] == "USD"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_malformed_currency_is_rejected_without_a_preview_or_a_state()
    {
        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "120000 dollars"));

        messages.ShouldHaveSingleItem();
        await _preview.DidNotReceive().CountAffectedAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _state.DidNotReceive().SetAsync(Arg.Any<long>(), Arg.Any<ConversationState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_zero_affected_count_still_previews_and_asks_to_confirm()
    {
        PreviewReturns(0);

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, "80000"));

        // No job is below the floor today, but the change is still previewed before it is made, never silently.
        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("no");
        await _state.Received(1).SetAsync(OwnerChat, Arg.Any<ConversationState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_confirm_reply_writes_the_previewed_floor_to_the_active_profile()
    {
        var profile = NewProfile();
        _profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns(profile);

        var messages = await NewHandler().ResumeAsync(ConfirmResume("150000", "USD"));

        // The parsed amount and currency the preview step stored are written to the active Profile at the
        // clock's time, then committed — the one write path for an explicit floor (F4 AC-05).
        profile.SalaryFloor.ShouldBe(150_000m);
        profile.SalaryFloorCurrency.ShouldBe("USD");
        profile.UpdatedAt.ShouldBe(Now);
        await _profiles.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // The confirmation names the applied floor so the Owner sees exactly what took effect.
        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("150000");
        text.ShouldContain("USD");
    }

    [Fact]
    public async Task A_confirm_reply_clears_the_pending_state()
    {
        _profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns(NewProfile());

        await NewHandler().ResumeAsync(ConfirmResume("120000", "EUR"));

        // The confirm step is terminal: the pending state is cleared so a later reply is not mistaken for it.
        await _state.Received(1).ClearAsync(OwnerChat, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_confirm_reply_with_no_active_profile_writes_nothing_and_clears_the_state()
    {
        _profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns((Profile?)null);

        var messages = await NewHandler().ResumeAsync(ConfirmResume("120000", "EUR"));

        // No Profile is active — say so plainly rather than throwing, commit nothing, and still clear the state.
        await _profiles.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _state.Received(1).ClearAsync(OwnerChat, Arg.Any<CancellationToken>());
        messages.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_non_confirm_reply_does_not_write_and_leaves_the_state_for_another_reply()
    {
        var messages = await NewHandler().ResumeAsync(new CommandResumeRequest(
            OwnerChat,
            "confirm",
            new Dictionary<string, string> { ["amount"] = "120000", ["currency"] = "EUR" },
            "no thanks"));

        // Anything but a confirmation neither writes nor clears — the Owner can still confirm or /cancel.
        await _profiles.DidNotReceive().FindActiveAsync(Arg.Any<CancellationToken>());
        await _profiles.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _state.DidNotReceive().ClearAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        messages.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_confirm_reply_with_a_malformed_context_writes_nothing_and_clears_the_state()
    {
        var messages = await NewHandler().ResumeAsync(new CommandResumeRequest(
            OwnerChat,
            "confirm",
            new Dictionary<string, string> { ["currency"] = "EUR" }, // no amount
            "confirm"));

        // A context that cannot be read back is a terminal dead-end: never a write, never a wedged chat.
        await _profiles.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _state.Received(1).ClearAsync(OwnerChat, Arg.Any<CancellationToken>());
        messages.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_null_resume_request_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(
            () => NewHandler().ResumeAsync(null!));
    }
}
