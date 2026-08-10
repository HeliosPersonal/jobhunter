using JobHunter.Domain.Ratings;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F4 T20 done-when 5 (data-model §rating_round_log): the append-only per-week idempotence gate for the weekly
/// rating loop. The load-bearing property is the unique <c>(week_start, chat_id)</c> constraint —
/// <see cref="RatingRoundLog.TryOpenAsync"/> returns <c>true</c> the first time a week is opened and
/// <c>false</c> for any redelivered or re-scheduled tick, so the Owner is prompted once and the ratings behind
/// <c>precision@10</c> are never double-counted. A different week, or a different chat, is a different round.
/// Requires Docker.
/// </summary>
public sealed class RatingRoundLogTests
{
    private const long OwnerChat = 4242;
    private static readonly DateTimeOffset WeekStart = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OpenedAt = new(2026, 8, 10, 7, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task Opening_a_week_the_first_time_returns_true_and_writes_one_row()
    {
        await using var database = await TestDatabase.CreateAsync();
        var log = new RatingRoundLog(new NpgsqlConnectionFactory(database.ConnectionString), new SequentialIdGenerator());

        var opened = await log.TryOpenAsync(WeekStart, OwnerChat, OpenedAt);

        opened.ShouldBeTrue();
        await using var ctx = database.CreateContext();
        var row = await ctx.Set<RatingRound>().SingleAsync();
        row.WeekStart.ShouldBe(WeekStart);
        row.ChatId.ShouldBe(OwnerChat);
        row.OpenedAt.ShouldBe(OpenedAt);
    }

    [RequiresDockerFact]
    public async Task Re_opening_the_same_week_returns_false_and_writes_no_second_row()
    {
        await using var database = await TestDatabase.CreateAsync();
        var log = new RatingRoundLog(new NpgsqlConnectionFactory(database.ConnectionString), new SequentialIdGenerator());

        var first = await log.TryOpenAsync(WeekStart, OwnerChat, OpenedAt);
        var second = await log.TryOpenAsync(WeekStart, OwnerChat, OpenedAt.AddHours(1));

        first.ShouldBeTrue();
        second.ShouldBeFalse();
        await using var ctx = database.CreateContext();
        (await ctx.Set<RatingRound>().CountAsync()).ShouldBe(1);
    }

    [RequiresDockerFact]
    public async Task A_different_week_opens_its_own_round()
    {
        await using var database = await TestDatabase.CreateAsync();
        var log = new RatingRoundLog(new NpgsqlConnectionFactory(database.ConnectionString), new SequentialIdGenerator());

        await log.TryOpenAsync(WeekStart, OwnerChat, OpenedAt);
        var nextWeek = await log.TryOpenAsync(WeekStart.AddDays(7), OwnerChat, OpenedAt.AddDays(7));

        nextWeek.ShouldBeTrue();
        await using var ctx = database.CreateContext();
        (await ctx.Set<RatingRound>().CountAsync()).ShouldBe(2);
    }
}
