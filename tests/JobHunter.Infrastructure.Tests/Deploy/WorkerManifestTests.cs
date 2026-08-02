using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Deploy;

/// <summary>
/// T14 AC: the <c>jobhunter-worker</c> Deployment must be <c>replicas: 1</c> with
/// <c>strategy: Recreate</c> — never two orchestrators alive at once (SAD §11 D2). These are
/// load-bearing manifest facts, not defaults, so they are asserted rather than trusted. The check is a
/// plain text scan of the base manifest, needing no YAML dependency or a live cluster.
/// </summary>
public sealed class WorkerManifestTests
{
    private static string WorkerDeploymentYaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "JobHunter.slnx")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("Could not locate the repository root (JobHunter.slnx).");
        var path = Path.Combine(dir.FullName, "k8s", "base", "worker", "deployment.yaml");
        File.Exists(path).ShouldBeTrue($"Expected the worker manifest at {path}.");
        return File.ReadAllText(path);
    }

    [Fact]
    public void WorkerManifest_declaresSingleReplica()
    {
        var yaml = WorkerDeploymentYaml();

        yaml.ShouldContain("replicas: 1");
    }

    [Fact]
    public void WorkerManifest_usesRecreateStrategy()
    {
        var yaml = WorkerDeploymentYaml();

        yaml.ShouldContain("type: Recreate");
    }
}
