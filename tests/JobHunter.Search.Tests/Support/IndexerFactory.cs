using JobHunter.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobHunter.Search.Tests.Support;

/// <summary>Builds a <see cref="TypesenseIndexer"/> over a stub handler for zero-network unit tests.</summary>
internal static class IndexerFactory
{
    public const string EnvironmentPrefix = "test";

    public static readonly string CollectionName = $"{EnvironmentPrefix}_jobhunter_jobs";

    public static TypesenseOptions Options() => new()
    {
        BaseUrl = "http://typesense.test:8108",
        ApiKey = "secret-key",
        EnvironmentPrefix = EnvironmentPrefix,
    };

    public static TypesenseIndexer Create(StubHttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://typesense.test:8108/") };
        return new TypesenseIndexer(http, Microsoft.Extensions.Options.Options.Create(Options()), NullLogger<TypesenseIndexer>.Instance);
    }

    public static TypesenseQueryService CreateQueryService(StubHttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://typesense.test:8108/") };
        return new TypesenseQueryService(
            http, Microsoft.Extensions.Options.Options.Create(Options()), NullLogger<TypesenseQueryService>.Instance);
    }
}
