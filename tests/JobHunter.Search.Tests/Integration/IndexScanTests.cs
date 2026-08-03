using System.Text.Json;
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
/// The no-CV-in-index scan (F9-T10, AC-04, QG-2), the safety suite this feature rests on. Every document
/// reaches the index through the one pure <see cref="JobDocumentProjection"/> — the hand-written
/// allowlist — so a CV sentinel woven into the aggregate the projection reads from can never appear in a
/// document, and no field beyond <see cref="JobDocument.FieldNames"/> can either. This runs that argument
/// against a real Typesense collection: it indexes a corpus whose free-text fields are laced with sentinel
/// tokens standing in for CV-derived and match-derived content, exports the whole collection, and asserts
/// (1) not one sentinel occurrence in any field of any document, and (2) the union of field names present
/// across every document is exactly a subset of the allowlist with none of the deliberately-absent
/// private fields. Step (2) is the one that catches a future widening of the projection. Skips cleanly
/// where Docker is absent.
/// </summary>
public sealed class IndexScanTests : IAsyncLifetime
{
    private const string ApiKey = "test-api-key";

    /// <summary>
    /// The tokens that would only ever appear in the index if a CV-derived or match-derived value leaked.
    /// <see cref="JobProjectionSource"/> — the single read the projection is a pure function of — exposes no
    /// member that could carry any of these, so the scan proves their absence structurally, not by luck.
    /// </summary>
    private static readonly string[] CvSentinels =
    [
        "SENTINEL-CV-7f3a91",
        "SENTINEL-MATCH-REASON-b28c",
        "SENTINEL-MISSING-SKILL-d41d",
        "SENTINEL-APPLICATION-NOTE-99aa",
    ];

    /// <summary>A sentinel placed in an allowlisted free-text field, so the scan is proven to read real
    /// content — if the export or the parse were empty, the liveness assertion below would fail rather than
    /// the CV assertion passing vacuously.</summary>
    private const string IndexedSentinel = "SENTINEL-INDEXED-DESCRIPTION-c0ffee";

    /// <summary>The private field names that reference the CV implicitly and must never be indexed (QG-2).</summary>
    private static readonly HashSet<string> ForbiddenFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "reasons", "matchReasons", "missingSkills", "applicationNotes", "notes",
        "interviewProbability", "preferenceWeights", "cv", "cvContent",
    };

    private readonly TypesenseContainer _container = new TypesenseBuilder("typesense/typesense:27.1")
        .WithApiKey(ApiKey)
        .Build();

    private TypesenseIndexer _indexer = null!;
    private Uri _baseUri = null!;
    private string _collection = null!;

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
        _collection = options.CollectionName;

        _baseUri = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        _indexer = new TypesenseIndexer(
            new HttpClient { BaseAddress = _baseUri }, Options.Create(options), NullLogger<TypesenseIndexer>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (DockerEnvironment.IsAvailable)
        {
            await _container.DisposeAsync();
        }
    }

    [RequiresDockerFact]
    public async Task The_index_contains_no_cv_sentinel_and_no_field_beyond_the_allowlist()
    {
        (await _indexer.EnsureCollectionAsync()).IsSuccess.ShouldBeTrue();

        // A corpus projected the only way a document is ever built: through the pure allowlist. One row's
        // description carries an indexed sentinel — proof the export reads real content — and no row can
        // carry a CV sentinel because JobProjectionSource has no member that could hold one.
        var documents = Corpus().Select(JobDocumentProjection.ToDocument).ToList();
        (await _indexer.UpsertManyAsync(documents)).IsSuccess.ShouldBeTrue();

        var exported = await ExportAllAsync();
        exported.Count.ShouldBe(documents.Count);

        var fieldsSeen = new HashSet<string>(StringComparer.Ordinal);
        var indexedSentinelSeen = false;
        foreach (var raw in exported)
        {
            using var doc = JsonDocument.Parse(raw);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                fieldsSeen.Add(property.Name);
            }

            indexedSentinelSeen |= raw.Contains(IndexedSentinel, StringComparison.Ordinal);

            // Not one CV/match/skill/note sentinel anywhere in any document — a CV-derived value would
            // surface here if the projection had ever gained a field that carried it.
            foreach (var sentinel in CvSentinels)
            {
                raw.ShouldNotContain(sentinel, Case.Sensitive, "a CV-derived sentinel leaked into the index");
            }
        }

        // Liveness: the scan really parsed indexed content, so the CV assertion above did not pass vacuously.
        indexedSentinelSeen.ShouldBeTrue("the indexed sentinel was not found — the export scan read nothing");

        // Every field the collection actually holds is one the allowlist names, and none is a private field
        // — nothing has widened the projection since (the assertion that catches the real leak: someone
        // maps the aggregate and everything comes along).
        foreach (var field in fieldsSeen)
        {
            JobDocument.FieldNameSet.ShouldContain(field, $"the index carries an unexpected field '{field}'");
            ForbiddenFieldNames.ShouldNotContain(field);
        }

        // matches.reasons and missing_skills are absent because both reference the CV implicitly.
        fieldsSeen.ShouldNotContain("reasons");
        fieldsSeen.ShouldNotContain("missingSkills");
    }

    private async Task<List<string>> ExportAllAsync()
    {
        // Typesense's export endpoint streams the whole collection as newline-delimited JSON documents.
        using var http = new HttpClient { BaseAddress = _baseUri };
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-TYPESENSE-API-KEY", ApiKey);
        var body = await http.GetStringAsync(
            new Uri($"collections/{_collection}/documents/export", UriKind.Relative));
        return [.. body.Split('\n', StringSplitOptions.RemoveEmptyEntries)];
    }

    private static IReadOnlyList<JobProjectionSource> Corpus() =>
    [
        // One row carries the indexed sentinel in an allowlisted free-text field, proving the export reads
        // real content. No row carries a CV sentinel — JobProjectionSource has nowhere to put one.
        Source("00000000-0000-7000-8000-000000000001", "Staff Backend Engineer",
            $"distributed systems in Go {IndexedSentinel}"),
        Source("00000000-0000-7000-8000-000000000002", "Senior Platform Engineer", "Kubernetes and Kafka"),
        Source("00000000-0000-7000-8000-000000000003", "Frontend Engineer", "TypeScript and React"),
    ];

    private static JobProjectionSource Source(string id, string title, string description) => new()
    {
        Id = Guid.Parse(id),
        Title = title,
        Description = description,
        Status = "Live",
        CompanyName = "Acme",
        CompanyDomain = "acme.com",
        Technologies = ["C#", ".NET"],
        Countries = ["DE"],
        RemotePolicy = "Remote",
        EmploymentType = "FullTime",
        FirstSeenAt = DateTimeOffset.FromUnixTimeSeconds(1_719_820_800),
    };
}
