using System.Diagnostics.Metrics;
using JobHunter.Application.Common;
using JobHunter.Application.Enrichment;
using JobHunter.Application.Matching;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Profiles;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;

namespace JobHunter.Application.Tests.Matching;

/// <summary>
/// T05: matching submission through the F3 Run machinery — the second spend-committing step and the one the
/// CV crosses into (F4 SAD §6.1, ADR-F3-0002). The ceiling gate is asserted identically to enrichment: a
/// breaching estimate must never reach the client (QG-2, invariant 6), proven with
/// <see cref="FakeLlmBatchClient.ThrowOnSubmit"/>. The estimate is ledgered before the client is called; a
/// redelivery neither resubmits nor re-estimates; and — unlike enrichment — the submit path does <em>not</em>
/// transition the Run, because the matching poller advances <c>Matching → Ranking</c> once results arrive.
/// The repository, scope query, request builder and the CV/Profile repositories are substituted, so these are
/// zero-database unit tests. The cost NFR (matching &lt; $0.60) is held one layer down, where the batch is
/// actually rendered and priced: <c>MatchRequestBuilderTests</c> prices a real <c>CostAccountant</c> against
/// the deep-tier table — the Application layer cannot reference <c>JobHunter.Claude</c> (architecture rule 3),
/// so it asserts the ceiling gate against a substituted <see cref="ICostAccountant"/> instead.
/// </summary>
public sealed class MatchingSubmitHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 2, 5, 0, TimeSpan.Zero);
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");
    private static readonly Guid ProfileId = Guid.Parse("00000000-0000-0000-0000-0000000000B1");
    private static readonly Guid CvVersionId = Guid.Parse("00000000-0000-0000-0000-0000000000C1");

    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly IMatchScopeQuery _scope = Substitute.For<IMatchScopeQuery>();
    private readonly IReMatchBacklog _reMatchBacklog = Substitute.For<IReMatchBacklog>();
    private readonly IMatchRequestBuilder _builder = Substitute.For<IMatchRequestBuilder>();
    private readonly IProfileRepository _profiles = Substitute.For<IProfileRepository>();
    private readonly ICvVersionRepository _cvVersions = Substitute.For<ICvVersionRepository>();
    private readonly ICurrentMatchQuery _currentMatches = Substitute.For<ICurrentMatchQuery>();
    private readonly IScoreRepository _scores = Substitute.For<IScoreRepository>();
    private readonly ICostAccountant _accountant = Substitute.For<ICostAccountant>();
    private readonly FakeLlmBatchClient _client = new();
    private readonly FakeClock _clock = new(Now);
    private readonly SequentialIdGenerator _ids = new();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly RunOptions _runOptions = new();
    private readonly PreMatchOptions _preMatchOptions = new();

    public MatchingSubmitHandlerTests()
    {
        // By default an active Profile and CV exist — the CV boundary is open. Tests that probe the
        // no-CV short-circuit override these to null.
        _profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns(ActiveProfile());
        _cvVersions.FindActiveAsync(ProfileId, Arg.Any<CancellationToken>()).Returns(ActiveCv());

        // By default the re-match backlog is empty — a CV change queues into it, and the tests that cover
        // the drain populate it explicitly.
        _reMatchBacklog.PendingJobIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Guid>());

        // By default no scoped job already carries a current match, so the pre-match filter's lifecycle rule
        // never bites and every job the scope returns reaches the deep tier. Tests that probe the filter
        // populate a Profile/enrichment that trips a rule, or seed a current match explicitly.
        _currentMatches.WithCurrentMatchAsync(
                Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid>());
    }

    private MatchingSubmitHandler CreateHandler(
        ICostAccountant? accountant = null,
        ILlmBatchClient? client = null,
        RunOptions? runOptions = null,
        PreMatchOptions? preMatchOptions = null) =>
        new(_runs, _scope, _reMatchBacklog, _builder, _profiles, _cvVersions, _currentMatches, _scores,
            accountant ?? _accountant, client ?? _client, _clock, _ids, runOptions ?? _runOptions,
            preMatchOptions ?? _preMatchOptions, NullLogger<MatchingSubmitHandler>.Instance);

    private List<object> Published() =>
        _bus.ReceivedCalls()
            .Select(c => c.GetArguments())
            .Where(a => a.Length > 0 && a[0] is not null)
            .Select(a => a[0]!)
            .ToList();

    /// <summary>A Run already in <see cref="RunState.Matching"/> — where <c>EnrichmentCompleted</c> finds it.</summary>
    private static Run MatchingRun(decimal ceiling = 2.00m, decimal spent = 0m)
    {
        var run = new Run(RunId, Now.AddHours(-24), Now, ceiling, Now.AddHours(-3));
        run.SetScope(2);
        if (spent > 0m)
        {
            run.SetSpend(spent);
        }

        run.TransitionTo(RunState.Enriching, Now);
        run.TransitionTo(RunState.Matching, Now);
        return run;
    }

    private static Profile ActiveProfile() =>
        new(ProfileId, isActive: true, "Owner", 120000m, "USD", TimezoneBand.EMEA,
            ["Portugal"], [EmploymentType.FullTime], Now.AddDays(-1));

    private static CvVersion ActiveCv(string text = "SENTINEL_CV_TEXT — fifteen years of backend engineering.") =>
        new(CvVersionId, ProfileId, version: 1, isActive: true, "cv.pdf", "application/pdf",
            sizeBytes: 2048, new string('a', 64), text, Now.AddDays(-1), Now.AddDays(-1));

    private static MatchJobContent Job(string title = "Backend Engineer", bool withEnrichment = true) =>
        new(
            Guid.CreateVersion7(), "Acme", "acme.com", title, "Senior", "Remote — EMEA",
            "USD 120000-160000 / Year", "FullTime", "We build things.",
            withEnrichment
                ? new MatchEnrichmentContent(
                    CompanyStage.SeriesB, IsRemote: true, TimezoneBand.EMEA, IsContractorFriendly: false,
                    EstimatedSalary: null, Technologies: ["C#", ".NET"], AiUsage: AiUsageLevel.Medium)
                : null);

    private void GivenScope(params MatchJobContent[] jobs)
    {
        _scope.InScopeAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(jobs.ToList());

        // The builder renders one item per job it is actually handed — so the batch reflects the survivors
        // the pre-match filter passed through, not the full scope.
        _builder.Build(Arg.Any<IReadOnlyList<MatchJobContent>>(), Arg.Any<Profile>(), Arg.Any<CvVersion>())
            .Returns(ci =>
            {
                var survivors = ci.Arg<IReadOnlyList<MatchJobContent>>() ?? [];
                var items = survivors
                    .Select(j => new BatchRequestItem(j.JobId.ToString(), "system", $"content-{j.Title}",
                        new JsonSchema("record_match", "{}")))
                    .ToList();
                return new MatchBatchRequest("match-v1", items, 550);
            });

        _runs.FindRetriableJobIdsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());
    }

    private void GivenEstimate(decimal costUsd, int inputTokens = 1000, int outputTokens = 700) =>
        _accountant.Estimate(Arg.Any<ModelTier>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>())
            .Returns(new CostEstimate(costUsd, inputTokens, outputTokens));

    /// <summary>
    /// A job the default Profile factually excludes: a Contract role, an employment type the Owner
    /// (<see cref="ActiveProfile"/> seeks only <see cref="EmploymentType.FullTime"/>) does not seek. It is a
    /// recognised type, so the employment-type rule bites — the cleanest single-rule exclusion for wiring tests.
    /// </summary>
    private static MatchJobContent ExcludedJob(string title = "Contract Engineer") =>
        new(
            Guid.CreateVersion7(), "Acme", "acme.com", title, "Senior", "Remote — EMEA",
            "USD 120000-160000 / Year", "Contract", "We build things.",
            new MatchEnrichmentContent(
                CompanyStage.SeriesB, IsRemote: true, TimezoneBand.EMEA, IsContractorFriendly: true,
                EstimatedSalary: null, Technologies: ["C#", ".NET"], AiUsage: AiUsageLevel.Medium));

    // ---- QG-2: the ceiling is a precondition, the client is never called on a breach ----------

    [Fact]
    public async Task Estimate_exceeding_ceiling_never_calls_the_client_and_aborts_the_run()
    {
        var run = MatchingRun(ceiling: 0.10m);
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        _runs.FindBatchAsync(RunId, BatchStage.Matching, ModelTier.Deep, Arg.Any<CancellationToken>())
            .Returns((Batch?)null);
        GivenScope(Job(), Job("Platform Engineer"));
        GivenEstimate(0.55m);

        // The tripwire: the test passes only if SubmitAsync is never reached (QG-2, invariant 6).
        _client.ThrowOnSubmit = true;

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 2, 0, Now), _bus, CancellationToken.None);

        _client.SubmitCallCount.ShouldBe(0);
        run.State.ShouldBe(RunState.CostAborted);
        run.FailureReason.ShouldNotBeNullOrWhiteSpace();
        Published().OfType<RunCostAborted>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Cost_abort_records_no_ledger_estimate_and_no_batch()
    {
        var run = MatchingRun(ceiling: 0.10m);
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(Job());
        GivenEstimate(0.55m);
        _client.ThrowOnSubmit = true;

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 1, 0, Now), _bus, CancellationToken.None);

        _runs.DidNotReceive().AddLedgerEntry(Arg.Any<CostLedgerEntry>());
        _runs.DidNotReceive().AddBatch(Arg.Any<Batch>());
    }

    [Fact]
    public async Task Spend_already_incurred_counts_toward_the_ceiling()
    {
        // Enrichment already spent 0.44; matching estimate alone fits but the projection breaches — aborts.
        var run = MatchingRun(ceiling: 0.60m, spent: 0.44m);
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(Job());
        GivenEstimate(0.20m);
        _client.ThrowOnSubmit = true;

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 1, 0, Now), _bus, CancellationToken.None);

        _client.SubmitCallCount.ShouldBe(0);
        run.State.ShouldBe(RunState.CostAborted);
    }

    // ---- The estimate is ledgered and committed BEFORE the client is called --------------------

    [Fact]
    public async Task Within_ceiling_ledgers_the_estimate_before_calling_the_client()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(Job(), Job("Platform Engineer"));
        GivenEstimate(0.44m);

        var events = new List<string>();
        _runs.When(r => r.AddLedgerEntry(Arg.Is<CostLedgerEntry>(e => e != null && e.Kind == LedgerEntryKind.Estimated)))
            .Do(_ => events.Add("ledger"));
        _runs.When(r => r.SaveChangesAsync(Arg.Any<CancellationToken>())).Do(_ => events.Add("save"));

        var submittingClient = new RecordingClient(events);
        await CreateHandler(client: submittingClient)
            .Handle(new EnrichmentCompleted(RunId, 2, 0, Now), _bus, CancellationToken.None);

        var ledgerIndex = events.IndexOf("ledger");
        var firstSaveAfterLedger = events.FindIndex(ledgerIndex, e => e == "save");
        var submitIndex = events.IndexOf("submit");
        ledgerIndex.ShouldBeGreaterThanOrEqualTo(0);
        submitIndex.ShouldBeGreaterThan(firstSaveAfterLedger);
    }

    [Fact]
    public async Task Within_ceiling_submits_persists_the_batch_and_leaves_the_run_in_matching()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(Job(), Job("Platform Engineer"));
        GivenEstimate(0.44m);
        _client.ProviderBatchId = "msgbatch_match_01";

        Batch? persisted = null;
        _runs.When(r => r.AddBatch(Arg.Any<Batch>())).Do(ci => persisted = ci.Arg<Batch>());

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 2, 0, Now), _bus, CancellationToken.None);

        _client.SubmitCallCount.ShouldBe(1);
        var submission = _client.LastSubmission.ShouldNotBeNull();
        submission.Tier.ShouldBe(ModelTier.Deep);
        submission.PromptVersion.ShouldBe("match-v1");
        submission.Items.Count.ShouldBe(2);

        var batch = persisted.ShouldNotBeNull();
        batch.Stage.ShouldBe(BatchStage.Matching);
        batch.Tier.ShouldBe(ModelTier.Deep);
        batch.ProviderBatchId.ShouldBe("msgbatch_match_01");
        batch.ItemCount.ShouldBe(2);

        // The submit path does NOT transition the Run — the poller advances Matching → Ranking (T06).
        run.State.ShouldBe(RunState.Matching);
    }

    [Fact]
    public async Task Within_ceiling_persists_one_batch_item_per_job_with_the_job_id_as_custom_id()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var a = Job();
        var b = Job("Platform Engineer");
        GivenScope(a, b);
        GivenEstimate(0.44m);

        var items = new List<BatchItem>();
        _runs.When(r => r.AddBatchItem(Arg.Any<BatchItem>())).Do(ci => items.Add((BatchItem)ci[0]!));

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 2, 0, Now), _bus, CancellationToken.None);

        items.Count.ShouldBe(2);
        items.Select(i => i.CustomId).ShouldBe([a.JobId.ToString(), b.JobId.ToString()], ignoreOrder: true);
        items.Select(i => i.JobId).ShouldBe([a.JobId, b.JobId], ignoreOrder: true);
    }

    [Fact]
    public async Task Within_ceiling_publishes_matching_submitted_and_poll_due()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(Job());
        GivenEstimate(0.44m);

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 1, 0, Now), _bus, CancellationToken.None);

        Published().OfType<MatchingBatchSubmitted>().ShouldHaveSingleItem();
        Published().OfType<MatchingPollDue>().ShouldHaveSingleItem();
    }

    // ---- AC-09: a job without an enrichment is matched, not skipped ----------------------------

    [Fact]
    public async Task An_enrichment_less_job_is_included_in_the_batch()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var enriched = Job();
        var bare = Job("Data Engineer", withEnrichment: false);
        GivenScope(enriched, bare);
        GivenEstimate(0.44m);

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 2, 0, Now), _bus, CancellationToken.None);

        _client.SubmitCallCount.ShouldBe(1);
        _client.LastSubmission.ShouldNotBeNull().Items.Count.ShouldBe(2);
    }

    // ---- The CV boundary: no active CV or Profile completes to Ranking without spending --------

    [Fact]
    public async Task No_active_cv_completes_to_ranking_without_calling_the_client()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        _cvVersions.FindActiveAsync(ProfileId, Arg.Any<CancellationToken>()).Returns((CvVersion?)null);
        GivenScope(Job());
        _client.ThrowOnSubmit = true;

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 1, 0, Now), _bus, CancellationToken.None);

        _client.SubmitCallCount.ShouldBe(0);
        run.State.ShouldBe(RunState.Ranking);
        Published().OfType<MatchingCompleted>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task No_active_profile_completes_to_ranking_without_calling_the_client()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        _profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns((Profile?)null);
        GivenScope(Job());
        _client.ThrowOnSubmit = true;

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 1, 0, Now), _bus, CancellationToken.None);

        _client.SubmitCallCount.ShouldBe(0);
        run.State.ShouldBe(RunState.Ranking);
        Published().OfType<MatchingCompleted>().ShouldHaveSingleItem();
    }

    // ---- T12 / ADR-F4-0003: the pre-match filter gates the deep tier ---------------------------

    [Fact]
    public async Task A_factually_excluded_job_is_recorded_suppressed_and_not_deep_matched()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var kept = Job();
        var dropped = ExcludedJob();
        GivenScope(kept, dropped);
        GivenEstimate(0.44m);

        Score? suppressed = null;
        await _scores.UpsertAsync(Arg.Do<Score>(s => suppressed = s), Arg.Any<CancellationToken>());

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 2, 0, Now), _bus, CancellationToken.None);

        // Only the survivor is priced and matched — the excluded job never reaches the deep tier.
        _client.SubmitCallCount.ShouldBe(1);
        _client.LastSubmission.ShouldNotBeNull().Items.Count.ShouldBe(1);

        // ...and the excluded job gets exactly one suppressed, reasoned, zero-total score row (invariant 11, AC-12).
        await _scores.Received(1).UpsertAsync(Arg.Any<Score>(), Arg.Any<CancellationToken>());
        var row = suppressed.ShouldNotBeNull();
        row.JobId.ShouldBe(dropped.JobId);
        row.RunId.ShouldBe(RunId);
        row.Suppressed.ShouldBeTrue();
        row.FinalScore.ShouldBe(0m);
        row.SuppressionReason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Every_scoped_job_excluded_completes_to_ranking_without_spending()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(ExcludedJob("Contract Engineer"), ExcludedJob("Contract Architect"));
        _client.ThrowOnSubmit = true;

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 2, 0, Now), _bus, CancellationToken.None);

        // Both jobs suppressed, nothing to judge: complete to Ranking without paying the provider (brief §9).
        _client.SubmitCallCount.ShouldBe(0);
        await _scores.Received(2).UpsertAsync(Arg.Any<Score>(), Arg.Any<CancellationToken>());
        run.State.ShouldBe(RunState.Ranking);
        Published().OfType<MatchingCompleted>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_job_already_matched_against_the_current_cv_is_excluded_on_lifecycle()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var kept = Job();
        var alreadyMatched = Job("Staff Engineer");
        GivenScope(kept, alreadyMatched);
        GivenEstimate(0.44m);
        _currentMatches.WithCurrentMatchAsync(
                CvVersionId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid> { alreadyMatched.JobId });

        Score? suppressed = null;
        await _scores.UpsertAsync(Arg.Do<Score>(s => suppressed = s), Arg.Any<CancellationToken>());

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 2, 0, Now), _bus, CancellationToken.None);

        _client.LastSubmission.ShouldNotBeNull().Items.Count.ShouldBe(1);
        suppressed.ShouldNotBeNull().JobId.ShouldBe(alreadyMatched.JobId);
    }

    [Fact]
    public async Task MatchAllJobs_calibration_pass_bypasses_the_filter_and_matches_everything()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(Job(), ExcludedJob());
        GivenEstimate(0.44m);

        var calibration = new RunOptions { MatchAllJobs = true };
        await CreateHandler(runOptions: calibration)
            .Handle(new EnrichmentCompleted(RunId, 2, 0, Now), _bus, CancellationToken.None);

        // The would-be-excluded job is matched anyway, and no suppression is recorded (AC-13): the bypass
        // measures what the filter would have hidden.
        _client.SubmitCallCount.ShouldBe(1);
        _client.LastSubmission.ShouldNotBeNull().Items.Count.ShouldBe(2);
        await _scores.DidNotReceive().UpsertAsync(Arg.Any<Score>(), Arg.Any<CancellationToken>());
        await _currentMatches.DidNotReceiveWithAnyArgs()
            .WithCurrentMatchAsync(default, default!, default);
    }

    [Fact]
    public async Task No_exclusions_writes_no_suppressed_rows()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(Job(), Job("Platform Engineer"));
        GivenEstimate(0.44m);

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 2, 0, Now), _bus, CancellationToken.None);

        _client.LastSubmission.ShouldNotBeNull().Items.Count.ShouldBe(2);
        await _scores.DidNotReceive().UpsertAsync(Arg.Any<Score>(), Arg.Any<CancellationToken>());
    }

    // ---- T21 / ADR-F4-0003: every exclusion is counted by rule (jobhunter.matching.prefiltered) ---

    [Fact]
    public async Task Each_excluded_job_increments_the_prefiltered_counter_tagged_with_its_rule()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        // One survivor plus two employment-type exclusions: the counter must record two, both tagged
        // EmploymentType, so a per-rule chart shows which rule is doing the excluding (T21 done-when 2).
        GivenScope(Job(), ExcludedJob("Contract Engineer"), ExcludedJob("Contract Architect"));
        GivenEstimate(0.44m);

        var measurements = await CapturePrefilteredAsync(() =>
            CreateHandler().Handle(new EnrichmentCompleted(RunId, 3, 0, Now), _bus, CancellationToken.None));

        measurements.Sum(m => m.Value).ShouldBe(2);
        measurements.ShouldAllBe(m => m.Rule == nameof(PreMatchRule.EmploymentType));
    }

    [Fact]
    public async Task No_exclusions_records_nothing_on_the_prefiltered_counter()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(Job(), Job("Platform Engineer"));
        GivenEstimate(0.44m);

        var measurements = await CapturePrefilteredAsync(() =>
            CreateHandler().Handle(new EnrichmentCompleted(RunId, 2, 0, Now), _bus, CancellationToken.None));

        measurements.ShouldBeEmpty();
    }

    /// <summary>
    /// Captures every measurement recorded on <c>jobhunter.matching.prefiltered</c> during <paramref name="act"/>,
    /// with the <c>rule</c> tag each carries, so a test can assert both the count and the rule attribution.
    /// </summary>
    private static async Task<IReadOnlyList<(long Value, string? Rule)>> CapturePrefilteredAsync(Func<Task> act)
    {
        var instrumentName = Telemetry.Prefiltered.Name;
        var measurements = new List<(long Value, string? Rule)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == instrumentName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            string? rule = null;
            foreach (var tag in tags)
            {
                if (tag.Key == TelemetryLabels.Rule)
                {
                    rule = tag.Value as string;
                }
            }

            measurements.Add((measurement, rule));
        });
        listener.Start();

        await act();

        return measurements;
    }

    // ---- AC-08: the previous Run's failed items retry once -------------------------------------

    [Fact]
    public async Task Carried_over_failed_items_are_included_in_the_scope_query()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var carried = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        GivenScope(Job());
        GivenEstimate(0.44m);
        _runs.FindRetriableJobIdsAsync(Arg.Any<CancellationToken>()).Returns(carried);

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 1, 0, Now), _bus, CancellationToken.None);

        await _scope.Received(1).InScopeAsync(
            run.CutoffFrom, run.CutoffTo,
            Arg.Is<IReadOnlyCollection<Guid>>(c => c != null && c.Count == 2 && c.Contains(carried[0]) && c.Contains(carried[1])),
            Arg.Any<CancellationToken>());
    }

    // ---- T09: the re-match backlog is drained into the scope and consumed ----------------------

    [Fact]
    public async Task Queued_re_match_jobs_are_folded_into_the_scope_and_marked_consumed()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var carried = new[] { Guid.CreateVersion7() };
        var reMatch = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        GivenScope(Job());
        GivenEstimate(0.44m);
        _runs.FindRetriableJobIdsAsync(Arg.Any<CancellationToken>()).Returns(carried);
        _reMatchBacklog.PendingJobIdsAsync(Arg.Any<CancellationToken>()).Returns(reMatch);

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 1, 0, Now), _bus, CancellationToken.None);

        // The re-match jobs join the carried-over failures in the scope union...
        await _scope.Received(1).InScopeAsync(
            run.CutoffFrom, run.CutoffTo,
            Arg.Is<IReadOnlyCollection<Guid>>(c =>
                c != null && c.Count == 3 &&
                c.Contains(carried[0]) && c.Contains(reMatch[0]) && c.Contains(reMatch[1])),
            Arg.Any<CancellationToken>());
        // ...and are marked consumed so a later Run does not re-match the same stale request.
        await _reMatchBacklog.Received(1).MarkConsumedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(c => c != null && c.Count == 2 && c.Contains(reMatch[0]) && c.Contains(reMatch[1])),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_empty_re_match_backlog_is_not_marked_consumed()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(Job());
        GivenEstimate(0.44m);

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 1, 0, Now), _bus, CancellationToken.None);

        await _reMatchBacklog.DidNotReceiveWithAnyArgs().MarkConsumedAsync(default!, default);
    }

    // ---- Idempotency (QG-1): a redelivery never resubmits --------------------------------------

    [Fact]
    public async Task An_already_submitted_batch_polls_rather_than_resubmitting()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var existing = new Batch(
            Guid.CreateVersion7(), RunId, BatchStage.Matching, ModelTier.Deep,
            "msgbatch_existing", "match-v1", 2, Now);
        _runs.FindBatchAsync(RunId, BatchStage.Matching, ModelTier.Deep, Arg.Any<CancellationToken>())
            .Returns(existing);
        _client.ThrowOnSubmit = true;

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 2, 0, Now), _bus, CancellationToken.None);

        _client.SubmitCallCount.ShouldBe(0);
        Published().OfType<MatchingPollDue>().ShouldHaveSingleItem();
        _runs.DidNotReceive().AddBatch(Arg.Any<Batch>());
    }

    [Fact]
    public async Task A_committed_estimate_from_a_prior_attempt_is_not_written_twice()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(Job());
        GivenEstimate(0.44m);
        _runs.HasLedgerEntryAsync(RunId, BatchStage.Matching, ModelTier.Deep, LedgerEntryKind.Estimated,
            Arg.Any<CancellationToken>()).Returns(true);

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 1, 0, Now), _bus, CancellationToken.None);

        _runs.DidNotReceive().AddLedgerEntry(Arg.Any<CostLedgerEntry>());
        _client.SubmitCallCount.ShouldBe(1);
    }

    // ---- D5 / checkpoint 4: adopt an unrecorded provider batch rather than resubmitting ---------

    [Fact]
    public async Task A_prior_attempt_that_left_an_unrecorded_provider_batch_is_adopted_not_resubmitted()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(Job());
        GivenEstimate(0.44m);
        _runs.HasLedgerEntryAsync(RunId, BatchStage.Matching, ModelTier.Deep, LedgerEntryKind.Estimated,
            Arg.Any<CancellationToken>()).Returns(true);

        _client.ProviderBatchId = "msgbatch_orphan_01";
        _client.ProviderCreatedAt = run.StartedAt.AddMinutes(1);
        await _client.SubmitAsync(
            new BatchSubmission(ModelTier.Deep, "match-v1", []), CancellationToken.None);
        var submitCallsBeforeResume = _client.SubmitCallCount;

        Batch? persisted = null;
        _runs.When(r => r.AddBatch(Arg.Any<Batch>())).Do(ci => persisted = ci.Arg<Batch>());

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 1, 0, Now), _bus, CancellationToken.None);

        _client.SubmitCallCount.ShouldBe(submitCallsBeforeResume);
        _client.ListCallCount.ShouldBe(1);
        persisted.ShouldNotBeNull().ProviderBatchId.ShouldBe("msgbatch_orphan_01");
        run.State.ShouldBe(RunState.Matching);
    }

    [Fact]
    public async Task A_prior_estimate_with_no_provider_batch_submits_exactly_once()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(Job());
        GivenEstimate(0.44m);
        _runs.HasLedgerEntryAsync(RunId, BatchStage.Matching, ModelTier.Deep, LedgerEntryKind.Estimated,
            Arg.Any<CancellationToken>()).Returns(true);

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 1, 0, Now), _bus, CancellationToken.None);

        _client.ListCallCount.ShouldBe(1);
        _client.SubmitCallCount.ShouldBe(1);
        run.State.ShouldBe(RunState.Matching);
    }

    [Fact]
    public async Task A_first_attempt_submits_without_a_reconciliation_read()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(Job());
        GivenEstimate(0.44m);

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 1, 0, Now), _bus, CancellationToken.None);

        _client.ListCallCount.ShouldBe(0);
        _client.SubmitCallCount.ShouldBe(1);
    }

    // ---- Edge cases -----------------------------------------------------------------------------

    [Fact]
    public async Task Empty_scope_at_submission_completes_to_ranking_without_spending()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenScope(); // nothing in scope now
        _client.ThrowOnSubmit = true;

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 0, 0, Now), _bus, CancellationToken.None);

        _client.SubmitCallCount.ShouldBe(0);
        run.State.ShouldBe(RunState.Ranking);
        Published().OfType<MatchingCompleted>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Unknown_run_is_ignored()
    {
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns((Run?)null);
        _client.ThrowOnSubmit = true;

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 0, 0, Now), _bus, CancellationToken.None);

        _client.SubmitCallCount.ShouldBe(0);
        Published().ShouldBeEmpty();
    }

    [Fact]
    public async Task Terminal_run_is_ignored()
    {
        var run = MatchingRun();
        run.Abort("already done", Now, costBreach: false);
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        _client.ThrowOnSubmit = true;

        await CreateHandler().Handle(new EnrichmentCompleted(RunId, 0, 0, Now), _bus, CancellationToken.None);

        _client.SubmitCallCount.ShouldBe(0);
    }

    /// <summary>A client that records the instant of its submit call into a shared ordering log.</summary>
    private sealed class RecordingClient(List<string> events) : ILlmBatchClient
    {
        public Task<string> SubmitAsync(BatchSubmission submission, CancellationToken cancellationToken)
        {
            events.Add("submit");
            return Task.FromResult("msgbatch_recording");
        }

        public Task<BatchStatus> GetStatusAsync(string providerBatchId, CancellationToken cancellationToken) =>
            Task.FromResult(new BatchStatus(ProviderBatchState.Ended, 0, 0, 0));

        public async IAsyncEnumerable<BatchResultItem> GetResultsAsync(
            string providerBatchId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<ProviderBatchRef>> ListRecentBatchesAsync(
            DateTimeOffset createdOnOrAfter, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderBatchRef>>([]);
    }
}
