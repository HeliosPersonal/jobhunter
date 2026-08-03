using System.Diagnostics.CodeAnalysis;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JobHunter.Search;

/// <summary>
/// The one composition method for the search adapter layer (coding-standards §3). Binds and validates
/// <see cref="TypesenseOptions"/> at startup (a missing base URL or api key fails the pod at boot), and
/// registers the named <see cref="HttpClient"/> the <see cref="TypesenseIndexer"/> and
/// <see cref="TypesenseQueryService"/> resolve. The write and read ports resolve to the same Typesense
/// adapter type family, so a host that indexes and a host that queries share one configuration. Excluded
/// from coverage — wiring is verified by the system starting.
/// </summary>
[ExcludeFromCodeCoverage]
public static class DependencyInjection
{
    public static IServiceCollection AddJobHunterSearch(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<TypesenseOptions>()
            .Bind(configuration.GetSection(TypesenseOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "Typesense:BaseUrl is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Typesense:ApiKey is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.EnvironmentPrefix), "Typesense:EnvironmentPrefix is required.")
            .ValidateOnStart();

        services.AddHttpClient(TypesenseIndexer.HttpClientName)
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<TypesenseOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                client.Timeout = options.RequestTimeout;
            });

        services.AddSingleton<ISearchIndex>(sp => new TypesenseIndexer(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(TypesenseIndexer.HttpClientName),
            sp.GetRequiredService<IOptions<TypesenseOptions>>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TypesenseIndexer>>()));

        return services;
    }
}
