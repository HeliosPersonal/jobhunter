using System.Diagnostics;

namespace JobHunter.TestKit;

/// <summary>
/// Detects, once per process, whether a Docker engine is reachable. Used by
/// <see cref="RequiresDockerFactAttribute"/> so Testcontainers-backed suites skip cleanly where Docker
/// is absent (developer laptop without Docker Desktop running, or a constrained CI leg) instead of
/// failing the whole run at container startup.
/// </summary>
public static class DockerEnvironment
{
    private static readonly Lazy<bool> Available = new(Probe);

    /// <summary>True when a Docker daemon responded to <c>docker info</c>.</summary>
    public static bool IsAvailable => Available.Value;

    private static bool Probe()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Process already exited between the check and the kill; nothing to do.
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The docker executable is not on PATH.
            return false;
        }
    }
}
