using Mono.Cecil;
using NetArchTest.Rules;

namespace JobHunter.ArchitectureTests;

/// <summary>
/// Architecture rule 8: a public type in <c>JobHunter.Infrastructure</c> must be one of the sanctioned
/// cross-project seams — a DI extension class, an <c>*Options</c> class, a factory, a configuration
/// helper, a repository/query, a DTO row, a boundary <c>*Exception</c> (thrown by a public adapter and
/// caught by its caller, e.g. the seed CLI) or a test attribute — never a leaked concrete service. The
/// internal adapters (e.g. the RabbitMQ replayer) satisfy this by being <c>internal</c>, so they never
/// reach this rule. Anything public that does not match the allowed shapes fails.
/// </summary>
public sealed class PublicInfrastructureTypeRule : ICustomRule
{
    private static readonly string[] AllowedSuffixes =
    [
        "Extensions",
        "Registration",
        "Configuration",
        "Options",
        "Factory",
        "Repository",
        "Query",
        "Row",
        "Registry",
        "DependencyInjection",
        "Attribute",
        "Exception",
    ];

    public bool MeetsRule(TypeDefinition type)
    {
        // Compiler-generated types (records' equality helpers, async state machines) are not our concern.
        if (type.Name.Contains('<', StringComparison.Ordinal) || type.Name.StartsWith('<'))
        {
            return true;
        }

        // Enums and interfaces used as ports are legitimate public surface.
        if (type.IsEnum || type.IsInterface)
        {
            return true;
        }

        // The EF Core seam is public by necessity: the DbContext, the mapped entities (aggregate roots
        // that must be constructible by migrations and tests) and the generated migrations themselves.
        if (DerivesFrom(type, "DbContext") || DerivesFrom(type, "Entity") || DerivesFrom(type, "Migration"))
        {
            return true;
        }

        // A DI extension class is static (abstract + sealed in IL); allow it by name below regardless.
        var name = type.Name;
        var plainName = name.Contains('`', StringComparison.Ordinal)
            ? name[..name.IndexOf('`', StringComparison.Ordinal)]
            : name;

        foreach (var suffix in AllowedSuffixes)
        {
            if (plainName.EndsWith(suffix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        // Records carrying data across the seam (e.g. RecurringJobRegistration) are allowed as DTOs.
        return plainName.EndsWith("Registration", StringComparison.Ordinal);
    }

    private static bool DerivesFrom(TypeDefinition type, string baseSimpleName)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (current.Name.Equals(baseSimpleName, StringComparison.Ordinal))
            {
                return true;
            }

            current = current.Resolve()?.BaseType;
        }

        return false;
    }
}
