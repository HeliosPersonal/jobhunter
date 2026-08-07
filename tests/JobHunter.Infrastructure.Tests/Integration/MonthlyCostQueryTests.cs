using JobHunter.Domain.Pipeline;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F10 T09 <c>/cost</c>: the month's spend rolled up by <c>(stage, tier)</c>, each line carrying both the
/// estimated and the actual dollars so the command can flag drift. The query sums the append-only cost ledger
/// over the half-open window <c>[monthStart, monthStart + 1 month)</c>, groups on the persisted text enum
/// names, and splits estimate from actual with a FILTER on <c>kind</c>. An entry recorded outside the window
/// is excluded — a month boundary is never double-counted. Requires Docker.
/// </summary>
public sealed class MonthlyCostQueryTests
{
    private static readonly DateTimeOffset MonthStart = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    private static Run NewRun() =>
        new(Guid.CreateVersion7(), MonthStart.AddDays(-1), MonthStart, ceilingUsd: 5.00m, MonthStart);

    private static CostLedgerEntry Entry(
        Guid runId, BatchStage stage, ModelTier tier, LedgerEntryKind kind, decimal costUsd, DateTimeOffset at) =>
        new(Guid.CreateVersion7(), runId, batchId: null, stage, tier, kind, costUsd, inputTokens: 100, outputTokens: 50, at);

    [RequiresDockerFact]
    public async Task It_sums_estimated_and_actual_by_stage_and_tier_within_the_month()
    {
        await using var database = await TestDatabase.CreateAsync();
        var run = NewRun();

        await using (var ctx = database.CreateContext())
        {
            ctx.Add(run);
            // Enrichment/Cheap: two estimates and one actual, all in-window.
            ctx.Add(Entry(run.Id, BatchStage.Enrichment, ModelTier.Cheap, LedgerEntryKind.Estimated, 0.10m, MonthStart.AddDays(2)));
            ctx.Add(Entry(run.Id, BatchStage.Enrichment, ModelTier.Cheap, LedgerEntryKind.Estimated, 0.05m, MonthStart.AddDays(3)));
            ctx.Add(Entry(run.Id, BatchStage.Enrichment, ModelTier.Cheap, LedgerEntryKind.Actual, 0.18m, MonthStart.AddDays(4)));
            // Matching/Deep: one estimate and one actual, in-window.
            ctx.Add(Entry(run.Id, BatchStage.Matching, ModelTier.Deep, LedgerEntryKind.Estimated, 1.00m, MonthStart.AddDays(5)));
            ctx.Add(Entry(run.Id, BatchStage.Matching, ModelTier.Deep, LedgerEntryKind.Actual, 1.30m, MonthStart.AddDays(6)));
            // Outside the window: the day before the month, and the first instant of the next month.
            ctx.Add(Entry(run.Id, BatchStage.Enrichment, ModelTier.Cheap, LedgerEntryKind.Actual, 9.99m, MonthStart.AddSeconds(-1)));
            ctx.Add(Entry(run.Id, BatchStage.Enrichment, ModelTier.Cheap, LedgerEntryKind.Actual, 9.99m, MonthStart.AddMonths(1)));
            await ctx.SaveChangesAsync();
        }

        var query = new MonthlyCostQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        var rows = await query.BreakdownForMonthAsync(MonthStart);

        rows.Count.ShouldBe(2);

        var enrichment = rows.Single(r => r.Stage == "Enrichment" && r.Tier == "Cheap");
        enrichment.EstimatedUsd.ShouldBe(0.15m);
        enrichment.ActualUsd.ShouldBe(0.18m);

        var matching = rows.Single(r => r.Stage == "Matching" && r.Tier == "Deep");
        matching.EstimatedUsd.ShouldBe(1.00m);
        matching.ActualUsd.ShouldBe(1.30m);
    }

    [RequiresDockerFact]
    public async Task It_returns_nothing_when_no_entry_falls_inside_the_month()
    {
        await using var database = await TestDatabase.CreateAsync();
        var run = NewRun();

        await using (var ctx = database.CreateContext())
        {
            ctx.Add(run);
            ctx.Add(Entry(run.Id, BatchStage.Enrichment, ModelTier.Cheap, LedgerEntryKind.Actual, 0.50m, MonthStart.AddMonths(-1)));
            await ctx.SaveChangesAsync();
        }

        var query = new MonthlyCostQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        var rows = await query.BreakdownForMonthAsync(MonthStart);

        rows.ShouldBeEmpty();
    }
}
