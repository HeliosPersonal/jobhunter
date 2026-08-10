using JobHunter.Domain.Ratings;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F4 T21 done-when 1 (ADR-F4-0003): the append-only per-week idempotence gate for the regret sampler, the
/// analogue of <see cref="RatingRoundLog"/> but keyed by <c>week_start</c> alone (the sample has a single Owner,
/// not a chat). The load-bearing property is the unique <c>week_start</c> constraint —
/// <see cref="RegretSampleLog.TryOpenAsync"/> returns <c>true</c> the first time a week is opened and
/// <c>false</c> for any redelivered or re-scheduled tick, so the cheap-tier sample runs once a week and never
/// double-spends. A different week is a different sample. Requires Docker.
/// </summary>
public sealed class RegretSampleLogTests
{
    private static readonly DateTimeOffset WeekStart = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OpenedAt = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task Opening_a_week_the_first_time_returns_true_and_writes_one_row()
    {
        await using var database = await TestDatabase.CreateAsync();
        var log = new RegretSampleLog(new NpgsqlConnectionFactory(database.ConnectionString), new SequentialIdGenerator());

        var opened = await log.TryOpenAsync(WeekStart, OpenedAt);

        opened.ShouldBeTrue();
        await using var ctx = database.CreateContext();
        var row = await ctx.Set<RegretSample>().SingleAsync();
        row.WeekStart.ShouldBe(WeekStart);
        row.OpenedAt.ShouldBe(OpenedAt);
    }

    [RequiresDockerFact]
    public async Task Re_opening_the_same_week_returns_false_and_writes_no_second_row()
    {
        await using var database = await TestDatabase.CreateAsync();
        var log = new RegretSampleLog(new NpgsqlConnectionFactory(database.ConnectionString), new SequentialIdGenerator());

        var first = await log.TryOpenAsync(WeekStart, OpenedAt);
        var second = await log.TryOpenAsync(WeekStart, OpenedAt.AddHours(1));

        first.ShouldBeTrue();
        second.ShouldBeFalse();
        await using var ctx = database.CreateContext();
        (await ctx.Set<RegretSample>().CountAsync()).ShouldBe(1);
    }

    [RequiresDockerFact]
    public async Task A_different_week_opens_its_own_sample()
    {
        await using var database = await TestDatabase.CreateAsync();
        var log = new RegretSampleLog(new NpgsqlConnectionFactory(database.ConnectionString), new SequentialIdGenerator());

        await log.TryOpenAsync(WeekStart, OpenedAt);
        var nextWeek = await log.TryOpenAsync(WeekStart.AddDays(7), OpenedAt.AddDays(7));

        nextWeek.ShouldBeTrue();
        await using var ctx = database.CreateContext();
        (await ctx.Set<RegretSample>().CountAsync()).ShouldBe(2);
    }
}
