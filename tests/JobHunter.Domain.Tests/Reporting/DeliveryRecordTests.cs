using JobHunter.Domain.Reporting;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Reporting;

public sealed class DeliveryRecordTests
{
    private static readonly Guid RecordId = Guid.Parse("00000000-0000-0000-0000-000000000011");
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");
    private static readonly Guid JobId = Guid.Parse("00000000-0000-0000-0000-0000000000D1");

    [Fact]
    public void A_card_record_exposes_its_fields()
    {
        var clock = new FakeClock();
        var key = CardKey.For(RunId, JobId);

        var record = new DeliveryRecord(RecordId, RunId, 4242L, key, 9001L, clock.UtcNow);

        record.Id.ShouldBe(RecordId);
        record.RunId.ShouldBe(RunId);
        record.ChatId.ShouldBe(4242L);
        record.CardKey.ShouldBe(key);
        record.TelegramMessageId.ShouldBe(9001L);
        record.DeliveredAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void The_header_and_footer_are_recorded_with_a_null_message_id()
    {
        var clock = new FakeClock();

        var header = new DeliveryRecord(RecordId, RunId, 4242L, CardKey.Header, null, clock.UtcNow);

        header.CardKey.IsReserved.ShouldBeTrue();
        header.TelegramMessageId.ShouldBeNull();
    }

    [Fact]
    public void Constructor_rejects_an_empty_run()
    {
        var clock = new FakeClock();

        Should.Throw<ArgumentException>(() =>
            new DeliveryRecord(RecordId, Guid.Empty, 1L, CardKey.Header, null, clock.UtcNow));
    }

    [Fact]
    public void Constructor_rejects_a_null_card_key()
    {
        var clock = new FakeClock();

        Should.Throw<ArgumentNullException>(() =>
            new DeliveryRecord(RecordId, RunId, 1L, null!, null, clock.UtcNow));
    }
}
