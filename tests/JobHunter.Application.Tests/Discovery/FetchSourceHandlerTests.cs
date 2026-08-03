using JobHunter.Application.Discovery;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Sources;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;

namespace JobHunter.Application.Tests.Discovery;

/// <summary>
/// T10/T11/T12: the per-source fetch handler resolves the source's live binding and provider adapter,
/// drains the board stream and ingests every posting through the dedup-and-refresh upsert. It publishes
/// <see cref="RawPostingIngested"/> exactly once per genuinely new posting (an unchanged re-fetch emits
/// nothing — AC-02) and is idempotent: replaying the same message re-ingests the same content and, because
/// the upsert reports <see cref="IngestOutcome.Unchanged"/>, publishes no second event. Every attempt writes
/// one <c>source_fetch_log</c> row (AC-11); a success resets the failure counter and the second consecutive
/// failure quarantines the source and publishes <see cref="SourceQuarantined"/> once (AC-08). It exits
/// cleanly — never faults — when the source was deleted in flight, the binding is gone or retired, or no
/// adapter is registered. Everything is stubbed, so this is a zero-network unit test.
/// </summary>
public sealed class FetchSourceHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    private readonly IJobSourceRepository _sources = Substitute.For<IJobSourceRepository>();
    private readonly ICompanyRepository _companies = Substitute.For<ICompanyRepository>();
    private readonly IJobSourceCatalog _catalog = Substitute.For<IJobSourceCatalog>();
    private readonly IRawPostingRepository _rawPostings = Substitute.For<IRawPostingRepository>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly SequentialIdGenerator _ids = new();
    private readonly FakeClock _clock = new(Now);
    private readonly DiscoveryOptions _options = new();

    private FetchSourceHandler CreateHandler() =>
        new(_sources, _companies, _catalog, _rawPostings, _ids, _clock, NullLogger<FetchSourceHandler>.Instance);

    private Task Handle(SourceFetchRequested request) =>
        CreateHandler().Handle(request, _bus, _options, CancellationToken.None);

    private static SourceFetchRequested Request(Guid sourceId, Guid companyId) =>
        new(sourceId, companyId, nameof(AtsKind.Greenhouse), Now, Now);

    private static JobSource Source(Guid sourceId, Guid companyId, Guid bindingId) =>
        new(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs");

    private static AtsBinding Binding(Guid bindingId, Guid companyId) =>
        new(bindingId, companyId, AtsKind.Greenhouse, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", Now);

    private (Guid SourceId, Guid CompanyId, Guid BindingId, JobSource Source) SeedLiveSource(StubJobSource adapter)
    {
        var sourceId = Guid.CreateVersion7();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var source = Source(sourceId, companyId, bindingId);

        _sources.FindAsync(sourceId, Arg.Any<CancellationToken>()).Returns(source);
        _companies.FindBindingAsync(bindingId, Arg.Any<CancellationToken>()).Returns(Binding(bindingId, companyId));
        _catalog.For(AtsKind.Greenhouse).Returns(adapter);

        return (sourceId, companyId, bindingId, source);
    }

    [Fact]
    public async Task It_fetches_the_board_for_the_resolved_live_binding()
    {
        var adapter = new StubJobSource(SourceFetch(FetchOutcome.Success, Posting("job-1")));
        var (sourceId, companyId, _, _) = SeedLiveSource(adapter);

        await Handle(Request(sourceId, companyId));

        adapter.FetchedBindings.ShouldHaveSingleItem();
        adapter.PostingsDrained.ShouldBe(1);
    }

    [Fact]
    public async Task A_genuinely_new_posting_is_ingested_and_publishes_exactly_one_event()
    {
        var adapter = new StubJobSource(SourceFetch(FetchOutcome.Success, Posting("job-1")));
        var (sourceId, companyId, _, _) = SeedLiveSource(adapter);
        _rawPostings.IngestAsync(Arg.Any<RawPosting>(), Arg.Any<CancellationToken>()).Returns(IngestOutcome.Inserted);

        var published = new List<RawPostingIngested>();
        await _bus.PublishAsync(Arg.Do<RawPostingIngested>(m => published.Add(m)));

        await Handle(Request(sourceId, companyId));

        await _rawPostings.Received(1).IngestAsync(Arg.Any<RawPosting>(), Arg.Any<CancellationToken>());
        var ingested = published.ShouldHaveSingleItem();
        ingested.SourceId.ShouldBe(sourceId);
        ingested.CompanyId.ShouldBe(companyId);
        ingested.ContentHash.ShouldBe(new string('a', 64));
    }

    [Fact]
    public async Task An_unchanged_re_fetch_ingests_but_publishes_nothing()
    {
        var adapter = new StubJobSource(SourceFetch(FetchOutcome.Success, Posting("job-1")));
        var (sourceId, companyId, _, _) = SeedLiveSource(adapter);
        _rawPostings.IngestAsync(Arg.Any<RawPosting>(), Arg.Any<CancellationToken>()).Returns(IngestOutcome.Unchanged);

        await Handle(Request(sourceId, companyId));

        await _rawPostings.Received(1).IngestAsync(Arg.Any<RawPosting>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<RawPostingIngested>());
    }

    [Fact]
    public async Task Replaying_the_same_message_publishes_no_second_event()
    {
        var adapter = new StubJobSource(SourceFetch(FetchOutcome.Success, Posting("job-1")));
        var (sourceId, companyId, _, _) = SeedLiveSource(adapter);

        // First delivery ingests the content (Inserted); the redelivery upserts the same content and the
        // xmax=0 trick reports Unchanged — so the replay publishes nothing (invariant 8).
        _rawPostings.IngestAsync(Arg.Any<RawPosting>(), Arg.Any<CancellationToken>())
            .Returns(IngestOutcome.Inserted, IngestOutcome.Unchanged);

        var published = new List<RawPostingIngested>();
        await _bus.PublishAsync(Arg.Do<RawPostingIngested>(m => published.Add(m)));

        var request = Request(sourceId, companyId);
        var handler = CreateHandler();
        await handler.Handle(request, _bus, _options, CancellationToken.None);
        await handler.Handle(request, _bus, _options, CancellationToken.None);

        published.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_posting_with_a_malformed_content_hash_is_skipped_not_ingested()
    {
        var adapter = new StubJobSource(SourceFetch(FetchOutcome.Success, new FetchedPosting("job-1", "{}", "not-a-hash")));
        var (sourceId, companyId, _, _) = SeedLiveSource(adapter);

        await Handle(Request(sourceId, companyId));

        await _rawPostings.DidNotReceive().IngestAsync(Arg.Any<RawPosting>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<RawPostingIngested>());
    }

    [Fact]
    public async Task Every_attempt_writes_exactly_one_fetch_log_row_including_transport_failures()
    {
        var adapter = new StubJobSource(SourceFetch(FetchOutcome.TransportError));
        var (sourceId, companyId, _, _) = SeedLiveSource(adapter);

        SourceFetchLog? logged = null;
        await _sources.AddFetchLogAsync(Arg.Do<SourceFetchLog>(l => logged = l), Arg.Any<CancellationToken>());

        await Handle(Request(sourceId, companyId));

        await _sources.Received(1).AddFetchLogAsync(Arg.Any<SourceFetchLog>(), Arg.Any<CancellationToken>());
        await _sources.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        logged.ShouldNotBeNull();
        logged!.SourceId.ShouldBe(sourceId);
        logged.StartedAt.ShouldBe(Now);
        logged.HttpStatus.ShouldBe((short)0);
        logged.Outcome.ShouldBe(FetchOutcome.TransportError);
    }

    [Fact]
    public async Task A_success_records_the_outcome_and_resets_the_failure_counter()
    {
        var adapter = new StubJobSource(SourceFetch(FetchOutcome.Success, Posting("job-1")));
        var (sourceId, companyId, _, source) = SeedLiveSource(adapter);
        source.RecordFailure(_clock, TimeSpan.FromHours(24)); // one prior failure on the books

        await Handle(Request(sourceId, companyId));

        source.ConsecutiveFailures.ShouldBe((short)0);
        source.QuarantinedUntil.ShouldBeNull();
    }

    [Fact]
    public async Task Two_consecutive_failures_quarantine_the_source_and_notify_once()
    {
        var adapter = new StubJobSource(SourceFetch(FetchOutcome.HttpError));
        var (sourceId, companyId, _, source) = SeedLiveSource(adapter);

        var quarantines = new List<SourceQuarantined>();
        await _bus.PublishAsync(Arg.Do<SourceQuarantined>(m => quarantines.Add(m)));

        // Two deliveries of the failing fetch: the first increments to 1 (healthy), the second crosses the
        // threshold and quarantines. The notification fires once, on the transition (AC-08).
        var handler = CreateHandler();
        await handler.Handle(Request(sourceId, companyId), _bus, _options, CancellationToken.None);
        await handler.Handle(Request(sourceId, companyId), _bus, _options, CancellationToken.None);

        source.QuarantinedUntil.ShouldBe(Now + _options.QuarantineFor);
        var quarantined = quarantines.ShouldHaveSingleItem();
        quarantined.SourceId.ShouldBe(sourceId);
        quarantined.CompanyId.ShouldBe(companyId);
        quarantined.ConsecutiveFailures.ShouldBe(2);
    }

    [Fact]
    public async Task A_single_failure_below_the_threshold_does_not_quarantine_or_notify()
    {
        var adapter = new StubJobSource(SourceFetch(FetchOutcome.HttpError));
        var (sourceId, companyId, _, source) = SeedLiveSource(adapter);

        await Handle(Request(sourceId, companyId));

        source.ConsecutiveFailures.ShouldBe((short)1);
        source.QuarantinedUntil.ShouldBeNull();
        await _bus.DidNotReceive().PublishAsync(Arg.Any<SourceQuarantined>());
    }

    [Fact]
    public async Task A_rate_deferral_is_logged_but_neither_fails_nor_resets_the_source()
    {
        var adapter = new StubJobSource(SourceFetch(FetchOutcome.RateLimited));
        var (sourceId, companyId, _, source) = SeedLiveSource(adapter);

        await Handle(Request(sourceId, companyId));

        source.ConsecutiveFailures.ShouldBe((short)0);
        source.QuarantinedUntil.ShouldBeNull();
        await _sources.Received(1).AddFetchLogAsync(Arg.Any<SourceFetchLog>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<SourceQuarantined>());
    }

    [Fact]
    public async Task A_source_deleted_while_the_message_is_in_flight_exits_cleanly()
    {
        var sourceId = Guid.CreateVersion7();
        _sources.FindAsync(sourceId, Arg.Any<CancellationToken>()).Returns((JobSource?)null);

        await Should.NotThrowAsync(() => Handle(Request(sourceId, Guid.CreateVersion7())));

        _catalog.DidNotReceive().For(Arg.Any<AtsKind>());
    }

    [Fact]
    public async Task A_source_whose_binding_was_retired_is_skipped()
    {
        var sourceId = Guid.CreateVersion7();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        _sources.FindAsync(sourceId, Arg.Any<CancellationToken>()).Returns(Source(sourceId, companyId, bindingId));

        var retired = Binding(bindingId, companyId);
        retired.Retire(new FakeClock(Now));
        _companies.FindBindingAsync(bindingId, Arg.Any<CancellationToken>()).Returns(retired);

        await Handle(Request(sourceId, companyId));

        _catalog.DidNotReceive().For(Arg.Any<AtsKind>());
    }

    [Fact]
    public async Task A_provider_with_no_registered_adapter_is_skipped_not_thrown()
    {
        var sourceId = Guid.CreateVersion7();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        _sources.FindAsync(sourceId, Arg.Any<CancellationToken>()).Returns(Source(sourceId, companyId, bindingId));
        _companies.FindBindingAsync(bindingId, Arg.Any<CancellationToken>()).Returns(Binding(bindingId, companyId));
        _catalog.For(AtsKind.Greenhouse).Returns((IJobSource?)null);

        await Should.NotThrowAsync(() => Handle(Request(sourceId, companyId)));
    }

    private static SourceFetch SourceFetch(FetchOutcome outcome, params FetchedPosting[] postings) =>
        new(outcome, (short)(outcome == FetchOutcome.Success ? 200 : 0), Stream(postings));

    private static FetchedPosting Posting(string id) =>
        new(id, "{\"id\":\"" + id + "\"}", new string('a', 64));

    private static async IAsyncEnumerable<FetchedPosting> Stream(FetchedPosting[] postings)
    {
        foreach (var posting in postings)
        {
            await Task.Yield();
            yield return posting;
        }
    }

    /// <summary>A hand-rolled adapter: records the bindings fetched and counts the postings drained.</summary>
    private sealed class StubJobSource(SourceFetch fetch) : IJobSource
    {
        private readonly SourceFetch _fetch = fetch;

        public List<AtsBinding> FetchedBindings { get; } = [];

        public int PostingsDrained { get; private set; }

        public AtsKind Kind => AtsKind.Greenhouse;

        public IAsyncEnumerable<FetchedPosting> FetchAsync(AtsBinding binding, CancellationToken cancellationToken) =>
            _fetch.Postings;

        public Task<SourceFetch> FetchBoardAsync(AtsBinding binding, CancellationToken cancellationToken)
        {
            FetchedBindings.Add(binding);
            return Task.FromResult(_fetch with { Postings = Count(_fetch.Postings) });
        }

        private async IAsyncEnumerable<FetchedPosting> Count(IAsyncEnumerable<FetchedPosting> inner)
        {
            await foreach (var posting in inner)
            {
                PostingsDrained++;
                yield return posting;
            }
        }
    }
}
