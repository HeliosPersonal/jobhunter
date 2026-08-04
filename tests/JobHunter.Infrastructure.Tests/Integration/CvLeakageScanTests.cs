using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using JobHunter.Application.Common;
using JobHunter.Application.Enrichment;
using JobHunter.Application.Matching;
using JobHunter.Application.Ranking;
using JobHunter.Application.Search;
using JobHunter.Claude;
using JobHunter.Claude.Matching;
using JobHunter.Claude.Prompts;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Common;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Profiles;
using JobHunter.Domain.Search;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;
using EnrichmentAggregate = JobHunter.Domain.Intelligence.Enrichment;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F4's centre of gravity for the one invariant whose violation would be genuinely damaging: the
/// <strong>CV leakage scan</strong> (testing-strategy §F4, test-plan §The leakage suite, QG-2, invariant:
/// the CV crosses exactly one boundary). A CV loaded with twelve unique sentinel tokens is driven through
/// the whole worktree pipeline — matching submission, polling, result processing, ranking and search
/// indexing — and <em>every</em> artifact the pipeline can emit is then scanned for any one of those tokens:
/// the logs (captured at every level, exceptions and stack traces included), the pipeline span attributes,
/// the search documents, and the stored <c>batch_items.raw_result</c> column. A single hit fails the build.
/// There is no allowlist and no sampling.
///
/// <para>The CV is materialised in exactly one place — <see cref="MatchRequestBuilder"/>, which folds
/// <see cref="CvVersion.ExtractedText"/> into each match item's user content — and that content is handed
/// straight to the provider client, never logged, never traced, never indexed, never persisted. These tests
/// are the executable proof of that claim. The suite is also proven <em>able to fail</em>: the same scanner,
/// pointed at a deliberately-leaking artifact, reports the hit — a scanner that can never fail would be worse
/// than none. The Docker-backed runs exercise the real Postgres read/write path; the scanner and fixture
/// guards run without Docker.</para>
/// </summary>
public sealed class CvLeakageScanTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 2, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The twelve unique sentinel tokens the fixture CV carries, spread across summary, employer names, a
    /// skill and project descriptions (test-plan §The leakage suite). Each is a string that appears nowhere
    /// else in the codebase, in no other fixture and in no job — so a single occurrence in any emitted
    /// artifact can only have come from the CV.
    /// </summary>
    private static readonly IReadOnlyList<string> Sentinels =
    [
        "ZQX-7F31-KAFKA-SENTINEL",
        "VULCAN-9920-AURORA-LEAK",
        "MERIDIAN-4417-COBALT-TRACE",
        "GRYPHON-5583-HELIUM-CANARY",
        "NIMBUS-3374-QUARTZ-BEACON",
        "OSPREY-8261-INDIGO-WARDEN",
        "FALCON-1195-ONYX-PHANTOM",
        "TITAN-6648-EMBER-LANTERN",
        "ZEPHYR-7702-SLATE-MIRAGE",
        "CASTOR-2039-VIOLET-SPECTRE",
        "POLLUX-8814-CRIMSON-VOYAGER",
        "DRAKON-5560-AMBER-SENTINEL",
    ];

    // ---- fixture integrity: the CV actually carries every sentinel (else the scan proves nothing) --------

    [Fact]
    public void The_sentinel_cv_fixture_carries_all_twelve_tokens()
    {
        var cv = LoadSentinelCv();

        foreach (var token in Sentinels)
        {
            cv.ShouldContain(token, Case.Sensitive, $"the fixture must carry sentinel {token}.");
        }

        Sentinels.Distinct(StringComparer.Ordinal).Count().ShouldBe(12, "the twelve sentinels must be unique.");
    }

    // ---- the scanner is proven able to fail: a deliberate leak is detected ------------------------------

    [Fact]
    public void The_scanner_detects_a_deliberately_introduced_leak()
    {
        // A single artifact that carries one sentinel — exactly the shape a real leak would take (a CV skill
        // that reached a log line). The scanner must catch it; a scanner that never fails is no gate at all.
        var leaked = new[]
        {
            ("log", $"Indexed job with skills including {Sentinels[11]} and Go."),
        };

        var hits = ScanForSentinels(leaked);

        hits.ShouldNotBeEmpty("a deliberately-introduced leak must be detected (proven able to fail).");
        hits.ShouldContain(h => h.Contains(Sentinels[11], StringComparison.Ordinal));
    }

    [Fact]
    public void The_scanner_passes_clean_artifacts_that_carry_no_sentinel()
    {
        var clean = new[]
        {
            ("log", "Submitted matching batch msgbatch_0001 for Run; 20 items."),
            ("span", "correlation.id=0193-abcd"),
            ("document", """{"title":"Staff Engineer","description":"We keep the lights on."}"""),
        };

        ScanForSentinels(clean).ShouldBeEmpty();
    }

    // ---- the whole pipeline, scanned end to end ---------------------------------------------------------

    [RequiresDockerFact]
    public async Task No_sentinel_reaches_any_emitted_artifact_across_the_whole_pipeline()
    {
        await using var h = await Harness.CreateAsync(LogLevel.Information);

        var collected = await h.RunPipelineAsync();

        // Every collected surface — logs, spans, search documents, stored raw results, exceptions — is scanned
        // as one set. A single sentinel in any of them fails the build (AC-06). No allowlist, no sampling.
        ScanForSentinels(collected).ShouldBeEmpty(
            "the CV crosses exactly one boundary; no sentinel may reach any emitted artifact.");

        // The pipeline genuinely ran: matches, scores and documents were produced against the sentinel CV.
        collected.ShouldContain(a => a.Source == "document");
        h.Documents.Count.ShouldBe(Harness.JobCount);
    }

    [RequiresDockerFact]
    public async Task No_sentinel_reaches_any_artifact_when_the_whole_pipeline_logs_at_debug()
    {
        // A leak that only surfaces during investigation is the worst kind, so the identical pipeline is run
        // with logging turned down to Debug and every line captured — nothing is filtered before the scan.
        await using var h = await Harness.CreateAsync(LogLevel.Debug);

        var collected = await h.RunPipelineAsync();

        collected.ShouldContain(a => a.Source == "log");
        ScanForSentinels(collected).ShouldBeEmpty("no sentinel may appear even at Debug verbosity.");
    }

    [RequiresDockerFact]
    public async Task A_forced_parse_failure_stores_a_raw_result_that_carries_no_sentinel()
    {
        // The model's raw output is the only thing kept in batch_items.raw_result, and only for failed items.
        // Forcing every item to fail parsing fills that column — and it must still hold no CV, because the
        // parser never sees the CV (the raw result is model output, not prompt input).
        await using var h = await Harness.CreateAsync(LogLevel.Debug, forceParseFailure: true);

        var collected = await h.RunPipelineAsync();

        // The failure path was actually taken: raw results were stored.
        collected.ShouldContain(a => a.Source == "raw_result");
        ScanForSentinels(collected).ShouldBeEmpty("a stored raw_result must carry no CV sentinel.");
    }

    [RequiresDockerFact]
    public async Task A_forced_indexing_failure_raises_an_exception_whose_message_and_stack_trace_carry_no_sentinel()
    {
        // Indexing failures throw SearchIndexingException (an infrastructure fault). Its message and full
        // stack trace are captured and scanned — a leak into an error path is still a leak (test-plan §The
        // leakage suite).
        await using var h = await Harness.CreateAsync(LogLevel.Debug, forceIndexFailure: true);

        var collected = await h.RunPipelineAsync();

        collected.ShouldContain(a => a.Source == "exception");
        ScanForSentinels(collected).ShouldBeEmpty("an exception message or stack trace must carry no CV sentinel.");
    }

    // ---- the scanner: the one place a hit is decided, no allowlist ---------------------------------------

    /// <summary>
    /// Returns one entry per (artifact, sentinel) hit, empty when nothing leaked. The comparison is
    /// case-insensitive so a lower-cased echo of the CV cannot slip past, and there is deliberately no
    /// allowlist: any occurrence, anywhere, is a hit.
    /// </summary>
    private static List<string> ScanForSentinels(IEnumerable<(string Source, string Content)> artifacts)
    {
        var hits = new List<string>();
        foreach (var (source, content) in artifacts)
        {
            if (string.IsNullOrEmpty(content))
            {
                continue;
            }

            foreach (var token in Sentinels)
            {
                if (content.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add($"{source}: {token}");
                }
            }
        }

        return hits;
    }

    private static string LoadSentinelCv() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "sentinel-cv.md"));

    // ====================================================================================================

    /// <summary>
    /// The leakage harness: a real Postgres database seeded with a Run in <c>Matching</c>, an active Profile
    /// and an active CV whose extracted text is the sentinel-laden fixture, plus a fixture-driven provider
    /// client. It captures everything the pipeline emits — logs (at the requested level, exceptions included),
    /// pipeline spans, search documents and stored raw results — so a single scan covers every surface. Each
    /// step runs through a fresh repository/context and is wrapped in a real <see cref="CorrelationScope"/>,
    /// exactly as the message bus would, so the span attributes scanned are the ones production would emit.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        public const int JobCount = 20;

        private static readonly PricingOptions Pricing = new()
        {
            Tiers = new Dictionary<string, TierPricing>
            {
                ["Cheap"] = new() { ModelId = "claude-haiku-4-5", InputPerMillion = 1.00m, OutputPerMillion = 5.00m, BatchDiscount = 0.5m },
                ["Deep"] = new() { ModelId = "claude-sonnet-5", InputPerMillion = 3.00m, OutputPerMillion = 15.00m, BatchDiscount = 0.5m },
            },
        };

        private readonly TestDatabase _db;
        private readonly NpgsqlConnectionFactory _factory;
        private readonly FakeClock _clock = new(Now);
        private readonly SequentialIdGenerator _ids = new();
        private readonly IJitter _jitter = Substitute.For<IJitter>();
        private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
        private readonly IReMatchBacklog _reMatchBacklog = Substitute.For<IReMatchBacklog>();
        private readonly FakeLlmBatchClient _client;
        private readonly InMemorySearchIndex _index = new();

        private readonly ConcurrentQueue<string> _logs = new();
        private readonly ConcurrentQueue<Activity> _activities = new();
        private readonly ILoggerFactory _loggerFactory;
        private readonly ActivityListener _listener;

        private readonly Guid _runId;
        private readonly IReadOnlyList<Guid> _jobIds;
        private readonly bool _forceIndexFailure;
        private readonly string _correlationId = Guid.CreateVersion7().ToString();
        private readonly List<string> _exceptions = [];

        public List<JobDocument> Documents => _index.Documents;

        private Harness(
            TestDatabase db,
            Guid runId,
            IReadOnlyList<Guid> jobIds,
            FakeLlmBatchClient client,
            LogLevel minLevel,
            bool forceIndexFailure)
        {
            _db = db;
            _factory = new NpgsqlConnectionFactory(db.ConnectionString);
            _runId = runId;
            _jobIds = jobIds;
            _client = client;
            _forceIndexFailure = forceIndexFailure;
            _index.FailUpserts = forceIndexFailure;

            _jitter.Apply(Arg.Any<TimeSpan>()).Returns(ci => ci.Arg<TimeSpan>());
            _reMatchBacklog.PendingJobIdsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());

            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(minLevel);
                builder.AddProvider(new CapturingLoggerProvider(_logs));
            });

            // Listen to the one pipeline ActivitySource and keep every stopped span, so the correlation
            // attributes the pipeline sets are available to the scan (there is no other span producer).
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == Telemetry.ActivitySourceName,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = _activities.Enqueue,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public static async Task<Harness> CreateAsync(
            LogLevel minLevel, bool forceParseFailure = false, bool forceIndexFailure = false)
        {
            var db = await TestDatabase.CreateAsync();
            var (runId, jobIds) = await SeedAsync(db);

            // One result per job. On the happy path each is a valid match record (score, band, missing skills,
            // salary, reasons) — model output, so sentinel-free by construction. The forced-failure variant
            // returns a malformed payload so every item is recorded ParseFailed with its raw_result kept.
            var results = jobIds.Select((id, i) => new BatchResultItem(
                    id.ToString(),
                    forceParseFailure ? MalformedResultJson : ValidResultJson(i),
                    ProviderError: null,
                    new TokenUsage(4000, 400)))
                .ToList();

            var client = new FakeLlmBatchClient(results)
            {
                ProviderBatchId = "msgbatch_leakscan_0001",
                ProviderCreatedAt = Now,
            };

            return new Harness(db, runId, jobIds, client, minLevel, forceIndexFailure);
        }

        /// <summary>
        /// Drives the full matching -> ranking -> indexing pipeline against the sentinel CV, then gathers every
        /// emitted artifact into one flat, source-tagged set for the scan.
        /// </summary>
        public async Task<IReadOnlyList<(string Source, string Content)>> RunPipelineAsync()
        {
            await SubmitAsync();
            await PollAsync();
            await ProcessResultsAsync();
            await RankAsync();
            await IndexAsync();

            return await CollectArtifactsAsync();
        }

        // ---- pipeline steps, each on a fresh context and wrapped in a real correlation scope ---------------

        private Task SubmitAsync() => InScopeAsync<MatchingSubmitHandler>(logger =>
            new MatchingSubmitHandler(
                    Runs(), new MatchScopeQuery(_factory), _reMatchBacklog, new MatchRequestBuilder(),
                    Profiles(), CvVersions(), Accountant(), _client, _clock, _ids, logger)
                .Handle(new EnrichmentCompleted(_runId, JobCount, 0, Now), _bus, CancellationToken.None));

        private Task PollAsync() => InScopeAsync<MatchingPollHandler>(logger =>
            new MatchingPollHandler(
                    Runs(), _client, _jitter, _clock,
                    new PollOptions { DeliveryDeadlineLocalTime = null, MaxPollDuration = TimeSpan.FromHours(6) },
                    logger)
                .Handle(new MatchingPollDue(_runId), _bus, CancellationToken.None));

        private Task ProcessResultsAsync() => InScopeAsync<MatchingResultProcessingHandler>(logger =>
            new MatchingResultProcessingHandler(
                    Runs(), Matches(), new MatchResultParser(), Profiles(), CvVersions(), _client, Accountant(),
                    _clock, _ids, logger)
                .Handle(new MatchingResultsReady(_runId, Guid.Empty, _client.ProviderBatchId), _bus, CancellationToken.None));

        private Task RankAsync() => InScopeAsync<RankingHandler>(logger =>
            new RankingHandler(
                    Runs(), new RankingScopeQuery(_factory), Profiles(), new NullPreferenceModelQuery(),
                    Scores(), new RankingOptions(), _clock, logger)
                .Handle(new MatchingCompleted(_runId, JobCount, 0, 0m, Now), _bus, CancellationToken.None));

        private async Task IndexAsync()
        {
            var handler = new SearchIndexingHandler(new JobProjectionQuery(_factory), _index, Logger<SearchIndexingHandler>());
            foreach (var jobId in _jobIds)
            {
                using var scope = CorrelationScope.Begin(
                    nameof(JobIndexRequested), _correlationId, Logger<SearchIndexingHandler>());
                try
                {
                    await handler.Handle(
                        new JobIndexRequested(jobId, JobIndexRequested.Upsert, Now), CancellationToken.None);
                }
                catch (SearchIndexingException ex) when (_forceIndexFailure)
                {
                    // The infrastructure fault the failure case forces. Its message and full stack trace are
                    // captured for the scan — a leak into an error path is still a leak.
                    _exceptions.Add(ex.ToString());
                }
            }
        }

        private async Task InScopeAsync<THandler>(Func<ILogger<THandler>, Task> step)
        {
            var logger = Logger<THandler>();
            using var scope = CorrelationScope.Begin(typeof(THandler).Name, _correlationId, logger);
            await step(logger);
        }

        // ---- artifact collection ---------------------------------------------------------------------------

        private async Task<IReadOnlyList<(string Source, string Content)>> CollectArtifactsAsync()
        {
            var artifacts = new List<(string, string)>();

            foreach (var log in _logs)
            {
                artifacts.Add(("log", log));
            }

            foreach (var activity in _activities)
            {
                artifacts.Add(("span", activity.DisplayName));
                foreach (var tag in activity.Tags)
                {
                    artifacts.Add(("span", $"{tag.Key}={tag.Value}"));
                }

                foreach (var evt in activity.Events)
                {
                    artifacts.Add(("span", evt.Name));
                }
            }

            foreach (var document in _index.Documents)
            {
                artifacts.Add(("document", JsonSerializer.Serialize(document)));
            }

            // batch_items.raw_result: the one persisted column that could conceivably carry model text.
            await using var ctx = _db.CreateContext();
            var rawResults = await ctx.Set<BatchItem>()
                .Where(i => i.RawResult != null)
                .Select(i => i.RawResult!)
                .ToListAsync();
            foreach (var raw in rawResults)
            {
                artifacts.Add(("raw_result", raw));
            }

            foreach (var ex in _exceptions)
            {
                artifacts.Add(("exception", ex));
            }

            return artifacts;
        }

        // ---- seeding ---------------------------------------------------------------------------------------

        private static async Task<(Guid RunId, IReadOnlyList<Guid> JobIds)> SeedAsync(TestDatabase db)
        {
            var companyId = Guid.CreateVersion7();
            var profileId = Guid.CreateVersion7();
            var jobIds = new List<Guid>(JobCount);

            await using (var ctx = db.CreateContext())
            {
                ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now));

                // The active Profile and the active CV whose extracted text is the sentinel-laden fixture. This
                // is the only place the sentinels enter the system.
                ctx.Add(new Profile(
                    profileId, isActive: true, "Owner", salaryFloor: null, salaryFloorCurrency: null,
                    TimezoneBand.EMEA, ["Portugal"], [EmploymentType.FullTime], Now));
                ctx.Add(new CvVersion(
                    Guid.CreateVersion7(), profileId, version: 1, isActive: true, "cv.pdf", "application/pdf",
                    sizeBytes: 4096, new string('a', 64), LoadSentinelCv(), Now, Now));

                for (var i = 0; i < JobCount; i++)
                {
                    var bindingId = Guid.CreateVersion7();
                    var sourceId = Guid.CreateVersion7();
                    var rawPostingId = Guid.CreateVersion7();
                    var jobId = Guid.CreateVersion7();
                    jobIds.Add(jobId);

                    ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, $"acme-{jobId:N}", BindingConfidence.TryCreate(0.9m).Value, "{}", Now));
                    ctx.Add(new JobSource(sourceId, companyId, bindingId, $"https://boards-api.greenhouse.io/v1/boards/acme-{jobId:N}/jobs"));
                    ctx.Add(new RawPosting(rawPostingId, sourceId, $"job-{jobId:N}", ContentHash.Compute($"{{\"t\":\"{jobId:N}\"}}"), "{\"t\":\"x\"}", 200, Now));
                    ctx.Add(new Job(
                        jobId, companyId, rawPostingId, Fingerprint.TryCreate(jobId.ToString("N") + Guid.NewGuid().ToString("N")).Value,
                        fingerprintVersion: 1, $"Staff Engineer {i}", normalisedTitle: $"staff engineer {i}",
                        description: "Build and operate distributed systems on Kubernetes.",
                        applyUrl: $"https://acme.com/apply/{jobId:N}",
                        LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
                        RemotePolicy.Remote, EmploymentType.FullTime, PostedAtGranularity.Day,
                        firstSeenAt: Now.AddHours(-2), lastSeenAt: Now.AddHours(-2)));
                }

                await ctx.SaveChangesAsync();
            }

            // A Run driven up to Matching, exactly the state the matching submit handler consumes.
            var runId = Guid.CreateVersion7();
            var runRepo = new RunRepository(db.CreateContext());
            var run = new Run(runId, Now.AddDays(-1), Now, ceilingUsd: 5m, Now);
            run.SetScope(JobCount);
            run.TransitionTo(RunState.Enriching, Now);
            run.TransitionTo(RunState.Matching, Now);
            runRepo.Add(run);
            await runRepo.SaveChangesAsync();

            return (runId, jobIds);
        }

        // A valid match record — model output, distinct per item, carrying no CV. Reasons and missing skills
        // are the model's judgement of the job, never the candidate's private text.
        private static string ValidResultJson(int i) =>
            $$"""
            {"matchScore":{{60 + (i % 30)}},"interviewProbability":"Good","missingSkills":["Rust"],
             "salaryExpectation":{"min":90000,"max":120000,"currency":"EUR","period":"Year"},
             "reasons":["Named Go as a core requirement, which the posting emphasises."]}
            """;

        // A payload the tolerant parser rejects (empty reasons violate invariant 4), so every item is recorded
        // ParseFailed with this raw text kept in batch_items.raw_result — still model output, no CV.
        private const string MalformedResultJson =
            """{"matchScore":75,"interviewProbability":"Good","missingSkills":[],"reasons":[]}""";

        private RunRepository Runs() => new(_db.CreateContext());

        private MatchRepository Matches() => new(_db.CreateContext(), _factory);

        private ScoreRepository Scores() => new(_db.CreateContext(), _factory);

        private ProfileRepository Profiles() => new(_db.CreateContext());

        private CvVersionRepository CvVersions() => new(_db.CreateContext());

        private static CostAccountant Accountant() => new(new HeuristicTokenCounter(), Options.Create(Pricing));

        private ILogger<T> Logger<T>() => _loggerFactory.CreateLogger<T>();

        public async ValueTask DisposeAsync()
        {
            _listener.Dispose();
            _loggerFactory.Dispose();
            await _db.DisposeAsync();
        }
    }

    /// <summary>An in-memory <see cref="ISearchIndex"/> that keeps the upserted documents for the scan, and can
    /// be forced to fail every upsert so the indexing error path is exercised.</summary>
    private sealed class InMemorySearchIndex : ISearchIndex
    {
        private readonly List<JobDocument> _documents = [];

        public List<JobDocument> Documents => _documents;

        public bool FailUpserts { get; set; }

        public Task<Result<bool>> EnsureCollectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<bool>.Success(true));

        public Task<Result<bool>> UpsertAsync(JobDocument document, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(document);
            if (FailUpserts)
            {
                return Task.FromResult(Result<bool>.Failure(new Error("index_unavailable", "forced failure for the leakage suite")));
            }

            _documents.RemoveAll(d => d.Id == document.Id);
            _documents.Add(document);
            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<Result<int>> UpsertManyAsync(IReadOnlyList<JobDocument> documents, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(documents);
            foreach (var document in documents)
            {
                _documents.RemoveAll(d => d.Id == document.Id);
                _documents.Add(document);
            }

            return Task.FromResult(Result<int>.Success(documents.Count));
        }

        public Task<Result<bool>> DeleteAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            _documents.RemoveAll(d => d.Id == jobId.ToString());
            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<Result<long>> CountAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<long>.Success(Documents.Count));

        public Task<Result<bool>> DropAndRecreateAsync(CancellationToken cancellationToken = default)
        {
            _documents.Clear();
            return Task.FromResult(Result<bool>.Success(true));
        }
    }

    /// <summary>An <see cref="ILoggerProvider"/> that captures every formatted message, rendered exception and
    /// scope state into a shared sink, so the scan sees exactly what a real log sink would.</summary>
    private sealed class CapturingLoggerProvider(ConcurrentQueue<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, sink);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<string> sink) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
            {
                sink.Enqueue($"scope {category}: {state}");
                return NullScope.Instance;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                ArgumentNullException.ThrowIfNull(formatter);
                sink.Enqueue($"{logLevel} {category}: {formatter(state, exception)}");
                if (exception is not null)
                {
                    sink.Enqueue(exception.ToString());
                }
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();

                public void Dispose()
                {
                }
            }
        }
    }
}
