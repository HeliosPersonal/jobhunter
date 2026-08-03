using System.Reflection;
using Shouldly;
using Xunit;

namespace JobHunter.ArchitectureTests;

/// <summary>
/// F4 T04 / QG-2: <c>MatchPrompt</c> is the one file in the whole codebase that renders CV text into a
/// string, so it is held to a structural rule — <strong>it has no <c>ILogger</c> and no
/// <c>ActivitySource</c>/<c>Activity</c> dependency, so it cannot emit the CV to a log or a span even by
/// accident</strong> (match-schema §CV handling rules 1–2, invariant: the CV crosses exactly one
/// boundary). This is enforced at the source level because the rule is about what the file <em>can
/// reach</em>, and it is deliberately paired with a reflection check that the type carries no telemetry
/// field either.
/// </summary>
public sealed class MatchPromptRulesTests
{
    private static readonly string[] CvBearingFiles = ["MatchPrompt.cs", "MatchPromptInput.cs"];

    private static readonly string[] BannedTokens =
    [
        "ILogger", "ILoggerFactory", "LoggerMessage", "ActivitySource", "Activity", "Telemetry", "Meter",
    ];

    [Fact]
    public void The_cv_rendering_files_reference_no_logger_or_telemetry_type()
    {
        var srcRoot = LocateSourceRoot();
        var offenders = new List<string>();

        foreach (var fileName in CvBearingFiles)
        {
            var path = Directory
                .EnumerateFiles(srcRoot, fileName, SearchOption.AllDirectories)
                .Single(p => !p.Replace('\\', '/').Contains("/obj/", StringComparison.Ordinal)
                             && !p.Replace('\\', '/').Contains("/bin/", StringComparison.Ordinal));

            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                // The rule is about code, not prose: a doc comment naming a banned token is not a use.
                if (trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("///", StringComparison.Ordinal)
                    || trimmed.StartsWith('*'))
                {
                    continue;
                }

                foreach (var token in BannedTokens)
                {
                    if (lines[i].Contains(token, StringComparison.Ordinal))
                    {
                        offenders.Add($"{fileName}:{i + 1}: {token} → {lines[i].Trim()}");
                    }
                }
            }
        }

        offenders.ShouldBeEmpty(
            "MatchPrompt renders CV text and must have no logger or telemetry dependency (QG-2): "
            + string.Join("; ", offenders));
    }

    [Fact]
    public void MatchPrompt_declares_no_logger_or_telemetry_member()
    {
        var type = typeof(JobHunter.Claude.Prompts.MatchPrompt);

        var members = type
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Select(m => (m as FieldInfo)?.FieldType ?? (m as PropertyInfo)?.PropertyType)
            .Where(t => t is not null)
            .Select(t => t!.Name)
            .ToList();

        members.ShouldNotContain(n => n.Contains("Logger", StringComparison.Ordinal));
        members.ShouldNotContain(n => n.Contains("Activity", StringComparison.Ordinal));
        members.ShouldNotContain(n => n.Contains("Meter", StringComparison.Ordinal));
    }

    private static string LocateSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "JobHunter.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate the repository root (JobHunter.slnx).");
        }

        return Path.Combine(dir.FullName, "src");
    }
}
