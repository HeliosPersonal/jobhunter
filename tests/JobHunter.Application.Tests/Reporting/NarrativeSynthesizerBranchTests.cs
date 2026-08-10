using JobHunter.Application.Reporting;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Reporting;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Reporting;

/// <summary>
/// F5 T05: the drain-and-bill arms of the market-note synthesiser that the primary suite in
/// <see cref="NarrativeSynthesizerTests"/> does not reach. A batch can end successfully yet return <em>no</em>
/// results — a provider that dropped the item — in which case there is no note to use but the submission was
/// still billed, so the Actual ledger entry is written and the digest falls back to the template. And an
/// already-committed estimate on a re-entry still lets a fresh run bill its Actual once. Everything is
/// substituted, so these stay zero-database, zero-network unit tests.
/// </summary>
public sealed class NarrativeSynthesizerBranchTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 2, 5, 0, TimeSpan.Zero);
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000A2");

    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly INarrativeRequestBuilder _requestBuilder = Substitute.For<INarrativeRequestBuilder>();
    private readonly INarrativeResultParser _resultParser = Substitute.For<INarrativeResultParser>();
    private readonly ICostAccountant _accountant = Substitute.For<ICostAccountant>();
    private readonly FakeClock _clock = new(Now);
    private readonly SequentialIdGenerator _ids = new();
    private readonly NarrativeSynthesisOptions _options = new();

    public NarrativeSynthesizerBranchTests()
    {
        _requestBuilder.Build(Arg.Any<NarrativeInput>()).Returns(_ => Request());
        _accountant.Estimate(Arg.Any<ModelTier>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>())
            .Returns(new CostEstimate(0.05m, 100, 50));
        _accountant.Actual(Arg.Any<ModelTier>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(new CostEstimate(0.04m, 0, 0));
        _resultParser.Parse(Arg.Any<string?>())
            .Returns(NarrativeParseOutcome.Success("A calm market."));
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(LiveRun());
        _runs.FindBatchAsync(RunId, BatchStage.Synthesis, ModelTier.Deep, Arg.Any<CancellationToken>())
            .Returns((Batch?)null);
    }

    private NarrativeSynthesizer CreateSynthesizer(ILlmBatchClient client) =>
        new(_runs, _requestBuilder, _resultParser, _accountant, client, _clock, _ids, _options,
            NullLogger<NarrativeSynthesizer>.Instance);

    private static NarrativeInput LiveDay() =>
        new(TotalNewJobs: 42, StrongMatches: 6, CardCount: 5, AvgSalaryUsd: 145000m,
            SuppressedCount: 11, CarriedOverCount: 3, DegradedSourceCount: 1);

    private static NarrativeBatchRequest Request() =>
        new("digest-narrative-v1",
            [new BatchRequestItem("digest-narrative", "system", "facts", new JsonSchema("record_market_note", "{}"))],
            MaxOutputTokensPerItem: 400);

    private static Run LiveRun() => new(RunId, Now.AddHours(-24), Now, 2.00m, Now.AddHours(-3));

    [Fact]
    public async Task An_ended_batch_that_returns_no_results_uses_the_template_but_still_bills_the_actual()
    {
        // The batch ends successfully but the provider yields no result item — the drain loop never runs, so no
        // note lands. The submission was billed regardless, so the Actual entry is written and the digest ships
        // from the template.
        var empty = new FakeLlmBatchClient(results: []);

        var result = await CreateSynthesizer(empty).SynthesizeAsync(RunId, LiveDay(), CancellationToken.None);

        result.Source.ShouldBe(NarrativeSource.Template);
        _runs.Received().AddLedgerEntry(Arg.Is<CostLedgerEntry>(e => e != null && e.Kind == LedgerEntryKind.Actual));
    }

    [Fact]
    public async Task An_ended_empty_batch_completes_the_batch_row()
    {
        // Reaching WriteActualCost on a fresh Submitted batch transitions it to Completed (its state was not
        // already terminal), so the batch is accounted for like any other even when its output was empty.
        Batch? added = null;
        _runs.When(r => r.AddBatch(Arg.Any<Batch>())).Do(call => added = call.Arg<Batch>());
        var empty = new FakeLlmBatchClient(results: []);

        await CreateSynthesizer(empty).SynthesizeAsync(RunId, LiveDay(), CancellationToken.None);

        added.ShouldNotBeNull();
        added.State.ShouldBe(BatchState.Completed);
    }
}
