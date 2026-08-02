using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace JobHunter.Infrastructure.Configuration;

/// <summary>
/// Startup secret bootstrap (SAD §6.1). In Development, Infisical is skipped entirely and the
/// Aspire-injected / user-secrets configuration is used as-is. In Staging/Production, the machine
/// identity must be present or the host fails fast with a non-zero exit (AC-09) — a misconfiguration
/// is a failed rollout at 14:00, not a failed Run at 02:00.
/// </summary>
[ExcludeFromCodeCoverage]
public static class SecretConfigurationExtensions
{
    public static IHostApplicationBuilder AddEnvVariablesAndConfigureSecrets(this IHostApplicationBuilder builder)
    {
        builder.Configuration.AddEnvironmentVariables();

        if (builder.Environment.IsDevelopment())
        {
            // Local dev: no Infisical, no fail-fast. Aspire/user-secrets provide everything.
            return builder;
        }

        var options = new InfisicalOptions();
        builder.Configuration.GetSection(InfisicalOptions.SectionName).Bind(options);

        if (!options.IsComplete)
        {
            throw new InvalidOperationException(
                "Infisical machine identity is required outside Development. " +
                "Set Infisical:ClientId, Infisical:ClientSecret and Infisical:ProjectId. " +
                "The host refuses to start with empty credentials (AC-09).");
        }

        // The concrete Infisical fetch is wired by the host once the SDK secrets are provisioned; the
        // fail-fast contract above is the load-bearing behaviour asserted by the smoke suite.
        return builder;
    }
}
