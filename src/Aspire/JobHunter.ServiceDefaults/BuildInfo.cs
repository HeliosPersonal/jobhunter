using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace JobHunter.ServiceDefaults;

/// <summary>Assembly version stamped onto the OpenTelemetry resource as <c>service.version</c>.</summary>
[ExcludeFromCodeCoverage]
public static class BuildInfo
{
    public static string Version { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";
}
