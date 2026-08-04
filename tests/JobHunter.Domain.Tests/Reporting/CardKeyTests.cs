using JobHunter.Domain.Reporting;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Reporting;

public sealed class CardKeyTests
{
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");
    private static readonly Guid JobId = Guid.Parse("00000000-0000-0000-0000-0000000000D1");

    [Fact]
    public void For_is_deterministic_across_calls()
    {
        var a = CardKey.For(RunId, JobId);
        var b = CardKey.For(RunId, JobId);

        a.ShouldBe(b);
        a.Value.ShouldBe(b.Value);
    }

    [Fact]
    public void For_is_stable_against_a_known_value()
    {
        // A regression guard: if the hashing ever changes, a resumed delivery would re-send every card.
        // This literal pins the algorithm across releases — sha256("N"(run) ‖ "N"(job))[..16]
        // (data-model §digest_cards "deterministic").
        var key = CardKey.For(RunId, JobId);

        key.Value.ShouldBe("67c48f7c70c7c764");
    }

    [Fact]
    public void For_differs_by_job()
    {
        var a = CardKey.For(RunId, JobId);
        var b = CardKey.For(RunId, Guid.Parse("00000000-0000-0000-0000-0000000000D2"));

        a.ShouldNotBe(b);
    }

    [Fact]
    public void For_differs_by_run()
    {
        var a = CardKey.For(RunId, JobId);
        var b = CardKey.For(Guid.Parse("00000000-0000-0000-0000-0000000000A2"), JobId);

        a.ShouldNotBe(b);
    }

    [Fact]
    public void For_is_sixteen_lowercase_hex_characters()
    {
        var key = CardKey.For(RunId, JobId);

        key.Value.Length.ShouldBe(16);
        key.Value.ShouldAllBe(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
    }

    [Fact]
    public void For_rejects_empty_ids()
    {
        Should.Throw<ArgumentException>(() => CardKey.For(Guid.Empty, JobId));
        Should.Throw<ArgumentException>(() => CardKey.For(RunId, Guid.Empty));
    }

    [Fact]
    public void The_reserved_keys_are_marked_reserved()
    {
        CardKey.Header.IsReserved.ShouldBeTrue();
        CardKey.Footer.IsReserved.ShouldBeTrue();
        CardKey.Header.Value.ShouldBe(CardKey.HeaderValue);
        CardKey.Footer.Value.ShouldBe(CardKey.FooterValue);
    }

    [Fact]
    public void A_job_key_is_not_reserved()
    {
        CardKey.For(RunId, JobId).IsReserved.ShouldBeFalse();
    }

    [Fact]
    public void TryCreate_rehydrates_a_valid_job_key()
    {
        var original = CardKey.For(RunId, JobId);

        var rehydrated = CardKey.TryCreate(original.Value);

        rehydrated.IsSuccess.ShouldBeTrue();
        rehydrated.Value.ShouldBe(original);
    }

    [Theory]
    [InlineData(CardKey.HeaderValue)]
    [InlineData(CardKey.FooterValue)]
    public void TryCreate_rehydrates_the_reserved_keys(string value)
    {
        var result = CardKey.TryCreate(value);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsReserved.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tooshort")]
    [InlineData("0123456789abcdef0")] // 17 chars
    [InlineData("0123456789ABCDEF")] // uppercase
    [InlineData("0123456789abcdeg")] // non-hex
    public void TryCreate_rejects_malformed_values(string? value)
    {
        var result = CardKey.TryCreate(value);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(CardKey.Invalid);
    }

    [Fact]
    public void ToString_is_the_value()
    {
        CardKey.For(RunId, JobId).ToString().ShouldBe(CardKey.For(RunId, JobId).Value);
    }
}
