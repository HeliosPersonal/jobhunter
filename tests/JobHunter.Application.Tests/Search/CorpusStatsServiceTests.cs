using JobHunter.Application.Search;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Common;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Search;

/// <summary>
/// The corpus snapshot behind <c>GET /api/admin/stats</c> (F9-T07, runbook R8): the authoritative
/// live-job count in PostgreSQL, the index document count and the drift between them — the same figure the
/// nightly reconcile acts on. The load-bearing behaviours: drift is the normalised divergence; an empty
/// corpus with an empty index is zero drift while an empty corpus with a populated index is total drift;
/// and an unreachable index never throws (QG-3) — the PostgreSQL truth is still reported with a null index.
/// </summary>
public sealed class CorpusStatsServiceTests
{
    private readonly ILiveJobCounter _counter = Substitute.For<ILiveJobCounter>();
    private readonly ISearchIndex _index = Substitute.For<ISearchIndex>();

    private CorpusStatsService NewService() =>
        new(_counter, _index, NullLogger<CorpusStatsService>.Instance);

    [Fact]
    public async Task Reports_live_indexed_and_normalised_drift_when_the_index_answers()
    {
        _counter.CountLiveAsync(Arg.Any<CancellationToken>()).Returns(200L);
        _index.CountAsync(Arg.Any<CancellationToken>()).Returns(Result<long>.Success(180L));

        var stats = await NewService().CollectAsync();

        stats.LiveJobs.ShouldBe(200L);
        stats.IndexedDocuments.ShouldBe(180L);
        stats.Drift.ShouldNotBeNull();
        stats.Drift.Value.ShouldBe(0.1d, 0.0001d);
        stats.IndexAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task An_empty_corpus_with_an_empty_index_is_zero_drift()
    {
        _counter.CountLiveAsync(Arg.Any<CancellationToken>()).Returns(0L);
        _index.CountAsync(Arg.Any<CancellationToken>()).Returns(Result<long>.Success(0L));

        var stats = await NewService().CollectAsync();

        stats.Drift.ShouldBe(0d);
        stats.IndexAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task An_empty_corpus_with_a_populated_index_is_total_drift()
    {
        _counter.CountLiveAsync(Arg.Any<CancellationToken>()).Returns(0L);
        _index.CountAsync(Arg.Any<CancellationToken>()).Returns(Result<long>.Success(5L));

        var stats = await NewService().CollectAsync();

        stats.Drift.ShouldBe(1d);
    }

    [Fact]
    public async Task An_unreachable_index_reports_the_postgres_truth_with_a_null_index_and_never_throws()
    {
        _counter.CountLiveAsync(Arg.Any<CancellationToken>()).Returns(42L);
        _index.CountAsync(Arg.Any<CancellationToken>())
            .Returns(Result<long>.Failure(new Error("index.unavailable", "down")));

        var stats = await NewService().CollectAsync();

        stats.LiveJobs.ShouldBe(42L);
        stats.IndexedDocuments.ShouldBeNull();
        stats.Drift.ShouldBeNull();
        stats.IndexAvailable.ShouldBeFalse();
    }
}
