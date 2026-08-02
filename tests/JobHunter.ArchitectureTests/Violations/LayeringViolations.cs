using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.ArchitectureTests.Violations;

// ---------------------------------------------------------------------------------------------------
// Deliberately-violating fixtures (T12). Each one embodies exactly one architecture rule broken, so
// ViolationFixturesTests can prove the corresponding guard goes red. They live in their own namespace,
// which every production rule test explicitly scopes away from (the real rules assert against the
// production assemblies / the src tree), and they are never referenced by production code.
// ---------------------------------------------------------------------------------------------------

/// <summary>Rule 1 broken: a Domain-shaped type reaching into Infrastructure.</summary>
public sealed class DomainDependsOnInfrastructure
{
    public static Type Leak => typeof(JobHunter.Infrastructure.DependencyInjection);
}

/// <summary>Rule 2 broken: an Application-shaped type reaching into Infrastructure.</summary>
public sealed class ApplicationDependsOnInfrastructure
{
    public static Type Leak => typeof(JobHunter.Infrastructure.DependencyInjection);
}

/// <summary>Rule 2b broken: an Infrastructure-shaped type reaching into a host.</summary>
public sealed class InfrastructureDependsOnWorkerHost
{
    public static Type Leak => typeof(JobHunter.Worker.WorkerHost);
}

/// <summary>
/// Rule 6 proxy: the AppHost assembly cannot be referenced from the test project by construction
/// (that is the whole point of the rule), so the guard mechanism is proven red against the API host
/// instead — the identical <c>NotHaveDependencyOn</c> assertion the real rule uses.
/// </summary>
public sealed class DependsOnApiHost
{
    public static Type Leak => typeof(JobHunter.Api.Program);
}

/// <summary>Rule 3 broken: a Contracts-shaped type depending on Domain.</summary>
public sealed class ContractsDependsOnDomain
{
    public static Type Leak => typeof(JobHunter.Domain.Abstractions.IClock);
}

/// <summary>
/// Rule 7 broken: an entity configuration that is <c>public</c> instead of <c>internal sealed</c>.
/// </summary>
public sealed class PublicEntityConfigurationViolation
    : IEntityTypeConfiguration<JobHunter.Infrastructure.Persistence.Reference.PlatformMarker>
{
    public void Configure(
        EntityTypeBuilder<JobHunter.Infrastructure.Persistence.Reference.PlatformMarker> builder)
    {
        // Intentionally empty: the violation is the accessibility, not the mapping body.
    }
}

/// <summary>
/// Rule 8 broken: a public, non-abstract type whose name matches none of the sanctioned Infrastructure
/// seams (extension / options / factory / configuration / repository / query / attribute). A leaked
/// concrete service is exactly what the rule forbids.
/// </summary>
public sealed class LeakedConcreteService
{
    public static string DoWork() => "leaked";
}
