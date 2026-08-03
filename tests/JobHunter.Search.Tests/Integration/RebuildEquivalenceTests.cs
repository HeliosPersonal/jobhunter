using JobHunter.Application.Search;
using JobHunter.Domain.Search;
using JobHunter.Search;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Testcontainers.Typesense;
using Xunit;

namespace JobHunter.Search.Tests.Integration;

/// <summary>
/// The rebuild equivalence suite (F9-T10, AC-10, QG-1) — the reason losing the index is a routine rebuild
/// rather than an incident. It indexes a corpus the normal way, snapshots every document, drops the
/// collection entirely, runs the one-command rebuild from the same PostgreSQL-shaped projection source, and
/// asserts <em>document-by-document</em> equivalence with the snapshot — not merely a matching count, which
/// a rebuild producing the right number of subtly-different documents would pass while failing the
/// product. It also asserts the rebuild reported itself inside the time budget. The projection source is a
/// zero-network fake standing in for the Dapper read; the round trip against Typesense is real. Skips
/// cleanly where Docker is absent.
/// </summary>
public sealed class RebuildEquivalenceTests : IAsyncLifetime
{
    private const string ApiKey = "test-api-key";
    private const int CorpusSize = 1_000;

    private readonly TypesenseContainer _container = new TypesenseBuilder("typesense/typesense:27.1")
        .WithApiKey(ApiKey)
        .Build();

    private TypesenseIndexer _indexer = null!;
    private TypesenseQueryService _service = null!;

    public async Task InitializeAsync()
    {
        if (!DockerEnvironment.IsAvailable)
        {
            return;
        }

        await _container.StartAsync();

        var options = new TypesenseOptions
        {
            BaseUrl = new UriBuilder(Uri.UriSchemeHttp, _container.Hostname, _container.GetMappedPublicPort(8108)).Uri.ToString(),
            ApiKey = ApiKey,
            EnvironmentPrefix = "test",
        };

        var baseUri = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        _indexer = new TypesenseIndexer(
            new HttpClient { BaseAddress = baseUri }, Options.Create(options), NullLogger<TypesenseIndexer>.Instance);
        _service = new TypesenseQueryService(
            new HttpClient { BaseAddress = baseUri }, Options.Create(options), NullLogger<TypesenseQueryService>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (DockerEnvironment.IsAvailable)
        {
            await _container.DisposeAsync();
        }
    }

    [RequiresDockerFact]
    public async Task A_full_rebuild_reconstructs_every_document_identically_within_budget()
    {
        var sources = Corpus();

        // 1. Index the corpus the normal way (the same UpsertMany the indexing handler drives).
        (await _indexer.EnsureCollectionAsync()).IsSuccess.ShouldBeTrue();
        var expected = sources.Select(JobDocumentProjection.ToDocument).ToList();
        (await _indexer.UpsertManyAsync(expected)).IsSuccess.ShouldBeTrue();

        // 2. Snapshot every document as it stands, keyed by id.
        var before = await SnapshotAsync();
        before.Count.ShouldBe(CorpusSize);

        // 3+4. Drop the collection entirely and run the one-command rebuild from the same projection source.
        var rebuild = new IndexRebuildService(
            new FakeProjectionSource(sources),
            _indexer,
            new IndexMaintenanceGate(),
            new FakeClock(new DateTimeOffset(2026, 8, 3, 4, 0, 0, TimeSpan.Zero)),
            Options.Create(new ReconcileOptions { BatchSize = 200 }),
            NullLogger<IndexRebuildService>.Instance);

        var report = await rebuild.RebuildAsync();

        report.IsSuccess.ShouldBeTrue();
        report.Value.Skipped.ShouldBeFalse();
        report.Value.Documents.ShouldBe(CorpusSize);
        report.Value.WithinBudget.ShouldBeTrue();

        // 5. Document-by-document equivalence with the snapshot — the assertion the product depends on.
        var after = await SnapshotAsync();
        after.Count.ShouldBe(before.Count);
        foreach (var (id, document) in before)
        {
            after.ShouldContainKey(id);
            // Deep structural equivalence over every field — including the collection members, which a
            // record's reference equality would spuriously distinguish even when their contents match.
            after[id].ShouldBeEquivalentTo(document);
        }
    }

    private async Task<Dictionary<string, JobDocument>> SnapshotAsync()
    {
        // Read every document back through the same query path a client uses, paging by the score cursor,
        // and materialise the domain JobDocument so equivalence is asserted on the allowlisted shape, not
        // on wire bytes. score:desc with the keyset cursor visits the whole corpus with no overlap.
        var snapshot = new Dictionary<string, JobDocument>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            var page = await _service.SearchAsync(new SearchQuery { Limit = 100, Cursor = cursor, IncludeClosed = true });
            page.IsSuccess.ShouldBeTrue();
            foreach (var hit in page.Value.Hits)
            {
                snapshot[hit.Document.Id] = hit.Document;
            }

            cursor = page.Value.NextCursor;
        }
        while (cursor is not null);

        return snapshot;
    }

    private static IReadOnlyList<JobProjectionSource> Corpus() =>
        [.. Enumerable.Range(0, CorpusSize).Select(Source)];

    private static JobProjectionSource Source(int i)
    {
        // A realistic spread so no two documents are trivially identical: distinct scores, technologies,
        // salaries and optionality, so "the right number of subtly-different documents" cannot pass.
        var tech = (i % 3) switch
        {
            0 => new[] { "C#", ".NET" },
            1 => new[] { "Go", "Kubernetes" },
            _ => new[] { "TypeScript", "React" },
        };

        return new JobProjectionSource
        {
            Id = Guid.Parse($"00000000-0000-7000-8000-{i:D12}"),
            Title = $"Engineer {i}",
            Description = $"Role {i} working with {string.Join(", ", tech)} and distributed systems.",
            Status = i % 7 == 0 ? "Closed" : "Live",
            CompanyName = $"Company {i % 50}",
            CompanyDomain = $"company{i % 50}.com",
            CompanyStage = i % 2 == 0 ? "SeriesB" : null,
            Technologies = tech,
            Countries = i % 4 == 0 ? [] : ["DE"],
            RemotePolicy = i % 2 == 0 ? "Remote" : "Hybrid",
            Seniority = i % 3 == 0 ? "Senior" : null,
            EmploymentType = "FullTime",
            SalaryMin = i % 5 == 0 ? null : 100_000 + i,
            SalaryMax = i % 5 == 0 ? null : 150_000 + i,
            SalaryCurrency = i % 5 == 0 ? null : "EUR",
            Score = i,
            PostedAt = i % 6 == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(1_719_820_800 + i),
            FirstSeenAt = DateTimeOffset.FromUnixTimeSeconds(1_719_820_800 + i),
        };
    }

    private sealed class FakeProjectionSource(IReadOnlyList<JobProjectionSource> rows) : Domain.Abstractions.IJobProjectionSource
    {
        public Task<JobProjectionSource?> ProjectAsync(Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(rows.FirstOrDefault(r => r.Id == jobId));

        public async IAsyncEnumerable<JobProjectionSource> ProjectLiveAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // The rebuild reconstructs the whole corpus regardless of status — the snapshot includes closed
            // jobs (IncludeClosed) so equivalence is over exactly the documents that were indexed.
            foreach (var row in rows)
            {
                yield return row;
                await Task.Yield();
            }
        }
    }
}
