using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Deploy;

/// <summary>
/// T07 AC: the <c>jobhunter-telegram</c> Deployment must be <c>replicas: 1</c> with
/// <c>strategy: Recreate</c> — two long-poll consumers would each receive half the updates, presenting as
/// randomly-ignored taps (SAD §7 D1). These are load-bearing manifest facts, not defaults, so they are
/// asserted rather than trusted. The check is a plain text scan of the base manifest, needing no YAML
/// dependency or a live cluster.
/// </summary>
public sealed class TelegramManifestTests
{
    private static string TelegramDeploymentYaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "JobHunter.slnx")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("Could not locate the repository root (JobHunter.slnx).");
        var path = Path.Combine(dir.FullName, "k8s", "base", "telegram", "deployment.yaml");
        File.Exists(path).ShouldBeTrue($"Expected the telegram manifest at {path}.");
        return File.ReadAllText(path);
    }

    [Fact]
    public void TelegramManifest_declaresSingleReplica()
    {
        var yaml = TelegramDeploymentYaml();

        yaml.ShouldContain("replicas: 1");
    }

    [Fact]
    public void TelegramManifest_usesRecreateStrategy()
    {
        var yaml = TelegramDeploymentYaml();

        yaml.ShouldContain("type: Recreate");
    }
}
