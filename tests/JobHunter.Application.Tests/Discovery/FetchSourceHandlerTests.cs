using JobHunter.Application.Discovery;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Sources;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Discovery;

/// <summary>
/// T10: the per-source fetch handler resolves the source's live binding and provider adapter and drains
/// the board stream. It exits cleanly — never faults — when the source was deleted while its message was
/// in flight, when the binding is gone or retired, or when no adapter is registered for the provider.
/// The adapter and repositories are stubbed, so this is a zero-network unit test.
/// </summary>
public sealed class FetchSourceHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    private readonly IJobSourceRepository _sources = Substitute.For<IJobSourceRepository>();
    private readonly ICompanyRepository _companies = Substitute.For<ICompanyRepository>();
    private readonly IJobSourceCatalog _catalog = Substitute.For<IJobSourceCatalog>();

    private FetchSourceHandler CreateHandler() =>
        new(_sources, _companies, _catalog, NullLogger<FetchSourceHandler>.Instance);

    private static SourceFetchRequested Request(Guid sourceId, Guid companyId) =>
        new(sourceId, companyId, nameof(AtsKind.Greenhouse), Now, Now);

    private static JobSource Source(Guid sourceId, Guid companyId, Guid bindingId) =>
        new(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs");

    private static AtsBinding Binding(Guid bindingId, Guid companyId) =>
        new(bindingId, companyId, AtsKind.Greenhouse, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", Now);

    [Fact]
    public async Task It_fetches_the_board_for_the_resolved_live_binding()
    {
        var sourceId = Guid.CreateVersion7();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();

        _sources.FindAsync(sourceId, Arg.Any<CancellationToken>()).Returns(Source(sourceId, companyId, bindingId));
        var binding = Binding(bindingId, companyId);
        _companies.FindBindingAsync(bindingId, Arg.Any<CancellationToken>()).Returns(binding);

        var adapter = new StubJobSource(SourceFetch(FetchOutcome.Success, Posting("job-1")));
        _catalog.For(AtsKind.Greenhouse).Returns(adapter);

        await CreateHandler().Handle(Request(sourceId, companyId), CancellationToken.None);

        adapter.FetchedBindings.ShouldHaveSingleItem().ShouldBe(binding);
        adapter.PostingsDrained.ShouldBe(1);
    }

    [Fact]
    public async Task A_source_deleted_while_the_message_is_in_flight_exits_cleanly()
    {
        var sourceId = Guid.CreateVersion7();
        _sources.FindAsync(sourceId, Arg.Any<CancellationToken>()).Returns((JobSource?)null);

        await Should.NotThrowAsync(() =>
            CreateHandler().Handle(Request(sourceId, Guid.CreateVersion7()), CancellationToken.None));

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

        await CreateHandler().Handle(Request(sourceId, companyId), CancellationToken.None);

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

        await Should.NotThrowAsync(() =>
            CreateHandler().Handle(Request(sourceId, companyId), CancellationToken.None));
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
