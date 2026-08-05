using JobHunter.Telegram.Transport;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Transport;

/// <summary>
/// The infrastructure-fault type a refused or throttled send raises (T07). It behaves like a plain
/// exception with an optional inner cause; the delivery handler catches it and leaves the log row unwritten
/// so the card retries without a double-send.
/// </summary>
public sealed class TelegramSendExceptionTests
{
    [Fact]
    public void It_carries_its_message()
    {
        var ex = new TelegramSendException("throttled");

        ex.Message.ShouldBe("throttled");
    }

    [Fact]
    public void It_carries_an_inner_cause()
    {
        var inner = new HttpRequestException("reset");
        var ex = new TelegramSendException("send failed", inner);

        ex.InnerException.ShouldBeSameAs(inner);
    }
}
