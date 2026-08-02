using Xunit;

namespace JobHunter.TestKit;

/// <summary>
/// A <see cref="FactAttribute"/> that skips itself when no Docker engine is reachable, so the
/// Testcontainers-backed integration and messaging suites are still compiled and shipped, but a
/// developer (or CI runner) without Docker sees a skip rather than a hard failure. When Docker is
/// present the test runs for real. The probe result is cached for the process.
/// </summary>
public sealed class RequiresDockerFactAttribute : FactAttribute
{
    public RequiresDockerFactAttribute()
    {
        if (!DockerEnvironment.IsAvailable)
        {
            Skip = "Docker is not available in this environment; Testcontainers integration test skipped.";
        }
    }
}

/// <summary>Companion <see cref="TheoryAttribute"/> that skips when Docker is unavailable.</summary>
public sealed class RequiresDockerTheoryAttribute : TheoryAttribute
{
    public RequiresDockerTheoryAttribute()
    {
        if (!DockerEnvironment.IsAvailable)
        {
            Skip = "Docker is not available in this environment; Testcontainers integration test skipped.";
        }
    }
}
