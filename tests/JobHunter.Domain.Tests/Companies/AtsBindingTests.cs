using JobHunter.Domain.Companies;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Companies;

public sealed class AtsBindingTests
{
    private static readonly Guid CompanyId = Guid.Parse("00000000-0000-0000-0000-0000000000AA");
    private static readonly Guid BindingId = Guid.Parse("00000000-0000-0000-0000-0000000000BB");
    private static readonly DateTimeOffset DetectedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static BindingConfidence Confidence(decimal value) => BindingConfidence.TryCreate(value).Value;

    private static AtsBinding NewBinding() =>
        new(BindingId, CompanyId, AtsKind.Greenhouse, "acme", Confidence(0.95m), "{}", DetectedAt);

    [Fact]
    public void TryCreate_succeeds_for_a_valid_binding()
    {
        var result = AtsBinding.TryCreate(
            BindingId, CompanyId, AtsKind.Lever, "acme", Confidence(0.9m), "{}", DetectedAt);

        result.IsSuccess.ShouldBeTrue();
        var binding = result.Value;
        binding.CompanyId.ShouldBe(CompanyId);
        binding.AtsKind.ShouldBe(AtsKind.Lever);
        binding.BoardToken.ShouldBe("acme");
        binding.Confidence.Value.ShouldBe(0.90m);
        binding.Evidence.ShouldBe("{}");
        binding.DetectedAt.ShouldBe(DetectedAt);
        binding.IsLive.ShouldBeTrue();
        binding.RetiredAt.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryCreate_fails_for_a_blank_board_token(string token)
    {
        var result = AtsBinding.TryCreate(
            BindingId, CompanyId, AtsKind.Ashby, token, Confidence(0.9m), "{}", DetectedAt);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(AtsBinding.EmptyBoardToken);
    }

    [Fact]
    public void Constructor_rejects_an_empty_company_id()
    {
        Should.Throw<ArgumentException>(() =>
            new AtsBinding(BindingId, Guid.Empty, AtsKind.Greenhouse, "acme", Confidence(0.9m), "{}", DetectedAt));
    }

    [Fact]
    public void Retire_sets_retired_at_from_the_clock()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
        var binding = NewBinding();

        binding.Retire(clock);

        binding.IsLive.ShouldBeFalse();
        binding.RetiredAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void Retire_is_idempotent_and_keeps_the_first_retirement_time()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
        var binding = NewBinding();

        binding.Retire(clock);
        var firstRetiredAt = binding.RetiredAt;

        clock.Advance(TimeSpan.FromDays(10));
        binding.Retire(clock);

        binding.RetiredAt.ShouldBe(firstRetiredAt);
    }
}
