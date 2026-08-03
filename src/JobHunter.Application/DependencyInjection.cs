using System.Diagnostics.CodeAnalysis;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Application;

/// <summary>
/// The one composition method for the Application layer. Registers the domain primitives every later
/// feature depends on. Wiring is verified by the system starting, so this type is excluded from coverage.
/// </summary>
[ExcludeFromCodeCoverage]
public static class DependencyInjection
{
    public static IServiceCollection AddJobHunterApplication(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, UuidV7Generator>();

        // Discovery application services — resolved by the CLI seed command and the recurring jobs.
        services.AddScoped<Discovery.CompanyRegistryService>();

        // DiscoveryOptions is passed to the cycle handler by Wolverine as a handler dependency; register
        // it as a resolvable singleton so both the handler and any consumer see one validated instance.
        services.AddOptions<Discovery.DiscoveryOptions>()
            .Validate(o => o.FetchConcurrency > 0, "Discovery:FetchConcurrency must be positive.")
            .Validate(o => o.RecentFetchWindow > TimeSpan.Zero, "Discovery:RecentFetchWindow must be positive.")
            .Validate(o => o.QuarantineFor > TimeSpan.Zero, "Discovery:QuarantineFor must be positive.")
            .ValidateOnStart();
        services.AddSingleton(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Discovery.DiscoveryOptions>>().Value);
        return services;
    }
}
