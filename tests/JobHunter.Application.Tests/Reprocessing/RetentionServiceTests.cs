using JobHunter.Application.Reprocessing;
using JobHunter.Domain.Abstractions;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Reprocessing;

/// <summary>
/// T09 / O3: the retention service decides the prune cutoff from <see cref="IClock"/> — 90 days by default,
/// never <c>DateTime.Now</c> — and delegates the actual, reference-safe delete to the repository. It never
/// removes a referenced posting itself; that guarantee lives in the repository's SQL and the restrict FK,
/// asserted by the Infrastructure integration test. Here we prove the cutoff arithmetic and the reporting.
/// </summary>
public sealed class RetentionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    private readonly IRawPostingRepository _rawPostings = Substitute.For<IRawPostingRepository>();
    private readonly FakeClock _clock = new(Now);

    private RetentionService CreateService() =>
        new(_rawPostings, _clock, NullLogger<RetentionService>.Instance);

    [Fact]
    public async Task It_prunes_postings_older_than_the_window_measured_from_the_clock()
    {
        _rawPostings.PruneOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(3);

        var pruned = await CreateService().PruneAsync(TimeSpan.FromDays(90), CancellationToken.None);

        pruned.ShouldBe(3);
        await _rawPostings.Received(1).PruneOlderThanAsync(Now.AddDays(-90), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_default_retention_window_is_ninety_days()
    {
        RetentionService.DefaultRetention.ShouldBe(TimeSpan.FromDays(90));

        await CreateService().PruneAsync(RetentionService.DefaultRetention, CancellationToken.None);

        await _rawPostings.Received(1).PruneOlderThanAsync(Now.AddDays(-90), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_non_positive_window_is_rejected(int days)
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => CreateService().PruneAsync(TimeSpan.FromDays(days), CancellationToken.None));

        await _rawPostings.DidNotReceiveWithAnyArgs().PruneOlderThanAsync(default, default);
    }
}
