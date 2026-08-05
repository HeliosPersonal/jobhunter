using JobHunter.TestKit;
using JobHunter.Telegram.Transport;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Transport;

/// <summary>
/// The pacing arithmetic (T07 AC: "sending paces to stay inside both rate limits and honours retry_after
/// exactly"), driven entirely by a <see cref="FakeClock"/> so nothing waits on real time. The load-bearing
/// rules: back-to-back reservations queue one minimum-interval apart; a reservation after the slot has
/// passed is immediate; and a 429 penalty pushes the slot out and is never shortened by our own spacing.
/// </summary>
public sealed class TelegramSendPacerTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    private static TelegramSendPacer Pacer(FakeClock clock) => new(clock, Interval);

    [Fact]
    public void The_first_reservation_may_send_immediately()
    {
        var pacer = Pacer(new FakeClock());

        pacer.ReserveSlot().ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void A_null_clock_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => new TelegramSendPacer(null!, Interval));
    }

    [Fact]
    public void Back_to_back_reservations_queue_one_interval_apart()
    {
        var pacer = Pacer(new FakeClock());

        pacer.ReserveSlot().ShouldBe(TimeSpan.Zero);
        // The clock has not moved, so the second slot is one full interval in the future.
        pacer.ReserveSlot().ShouldBe(Interval);
        pacer.ReserveSlot().ShouldBe(Interval * 2);
    }

    [Fact]
    public void A_reservation_after_the_slot_has_passed_is_immediate()
    {
        var clock = new FakeClock();
        var pacer = Pacer(clock);

        pacer.ReserveSlot();               // reserves now; next slot at now + interval
        clock.Advance(Interval * 2);       // real time has moved past the reserved slot

        pacer.ReserveSlot().ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void A_penalty_defers_the_next_send_by_the_retry_after()
    {
        var pacer = Pacer(new FakeClock());

        pacer.Penalise(TimeSpan.FromSeconds(3));

        pacer.ReserveSlot().ShouldBe(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void A_shorter_penalty_never_shortens_a_longer_block()
    {
        var pacer = Pacer(new FakeClock());

        pacer.Penalise(TimeSpan.FromSeconds(10));
        pacer.Penalise(TimeSpan.FromSeconds(1));   // a shorter cool-off must not override the longer one

        pacer.ReserveSlot().ShouldBe(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void A_penalty_takes_precedence_over_ordinary_spacing()
    {
        var pacer = Pacer(new FakeClock());

        pacer.ReserveSlot();                       // ordinary next slot at now + 50ms
        pacer.Penalise(TimeSpan.FromSeconds(2));   // the 429 pushes it far past that

        pacer.ReserveSlot().ShouldBe(TimeSpan.FromSeconds(2));
    }
}
