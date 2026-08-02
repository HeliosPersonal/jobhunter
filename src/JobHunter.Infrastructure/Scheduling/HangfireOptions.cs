namespace JobHunter.Infrastructure.Scheduling;

/// <summary>
/// Hangfire wiring options (T09). Hangfire runs in the same PostgreSQL under the <c>hangfire</c> schema
/// (ADR-0004); the server is hosted in the Worker only. The dashboard is cluster-internal and gated on
/// the <c>jobhunter:admin</c> scope — never exposed through the ingress.
/// </summary>
public sealed class HangfireOptions
{
    public const string SectionName = "Hangfire";

    /// <summary>Whether this host runs the Hangfire background server. True only in the Worker.</summary>
    public bool EnableServer { get; init; }

    /// <summary>Whether to map the dashboard. Cluster-internal, admin-scoped, port-forward only.</summary>
    public bool EnableDashboard { get; init; }

    /// <summary>The schema the Hangfire tables live in.</summary>
    public string SchemaName { get; init; } = "hangfire";

    /// <summary>Worker count for the background server.</summary>
    public int WorkerCount { get; init; } = 4;
}
