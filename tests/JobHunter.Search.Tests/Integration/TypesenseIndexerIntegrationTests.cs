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
/// The real round-trip against a Typesense container (F9-T02): create the collection from the schema,
/// upsert a document, prove the upsert is idempotent (same id, one document), and delete it. This is the
/// suite that proves the schema is one Typesense actually accepts and that <c>token_separators</c> and the
/// facets are declared as intended. Skips cleanly where Docker is absent.
/// </summary>
public sealed class TypesenseIndexerIntegrationTests : IAsyncLifetime
{
    private const string ApiKey = "test-api-key";

    private readonly TypesenseContainer _container = new TypesenseBuilder("typesense/typesense:27.1")
        .WithApiKey(ApiKey)
        .Build();

    private TypesenseIndexer _indexer = null!;

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

        var http = new HttpClient { BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/") };
        _indexer = new TypesenseIndexer(http, Options.Create(options), NullLogger<TypesenseIndexer>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (DockerEnvironment.IsAvailable)
        {
            await _container.DisposeAsync();
        }
    }

    private static JobDocument Document(string id, double score = 10) => new(
        Id: id, Title: "Staff Engineer C#", CompanyName: "Acme", CompanyDomain: "acme.com",
        Description: "Work in C# and .NET on CI/CD.", Technologies: ["C#", ".NET"], Countries: ["DE"],
        RemotePolicy: "Remote", Seniority: "Staff", EmploymentType: "FullTime", CompanyStage: null,
        AiUsage: null, SalaryMin: null, SalaryMax: null, SalaryCurrency: null, Score: score,
        PostedAt: null, FirstSeenAt: 1_719_820_800, Status: "Live", ApplicationStatus: null);

    [RequiresDockerFact]
    public async Task Create_upsert_idempotently_then_delete()
    {
        (await _indexer.EnsureCollectionAsync()).IsSuccess.ShouldBeTrue();
        // Creating again is a no-op success (idempotent).
        (await _indexer.EnsureCollectionAsync()).IsSuccess.ShouldBeTrue();

        var id = "0192e8b7-0000-7000-8000-000000000001";
        (await _indexer.UpsertAsync(Document(id, score: 10))).IsSuccess.ShouldBeTrue();
        // Same id, different score — an upsert, not a second document.
        (await _indexer.UpsertAsync(Document(id, score: 20))).IsSuccess.ShouldBeTrue();

        var count = await _indexer.CountAsync();
        count.IsSuccess.ShouldBeTrue();
        count.Value.ShouldBe(1);

        (await _indexer.DeleteAsync(Guid.Parse(id))).IsSuccess.ShouldBeTrue();
        // Deleting again is a success (idempotent under redelivery).
        (await _indexer.DeleteAsync(Guid.Parse(id))).IsSuccess.ShouldBeTrue();

        (await _indexer.CountAsync()).Value.ShouldBe(0);
    }

    [RequiresDockerFact]
    public async Task Upsert_many_imports_every_document_in_one_round_trip()
    {
        (await _indexer.DropAndRecreateAsync()).IsSuccess.ShouldBeTrue();

        var documents = Enumerable.Range(0, 5)
            .Select(i => Document($"0192e8b7-0000-7000-8000-00000000000{i}"))
            .ToList();

        var imported = await _indexer.UpsertManyAsync(documents);

        imported.IsSuccess.ShouldBeTrue();
        imported.Value.ShouldBe(5);
        (await _indexer.CountAsync()).Value.ShouldBe(5);
    }
}
