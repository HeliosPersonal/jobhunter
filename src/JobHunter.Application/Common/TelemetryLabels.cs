namespace JobHunter.Application.Common;

/// <summary>
/// The closed set of metric label keys allowed on the domain instruments (observability §2). Ids
/// (<c>job_id</c>, <c>company_id</c>, <c>run_id</c>) are forbidden as labels — unbounded cardinality
/// would exhaust the Grafana Cloud free tier; those belong on spans as attributes. T11 asserts no
/// instrument accepts an id-shaped label by checking every emitted key against this set.
/// </summary>
public static class TelemetryLabels
{
    public const string Stage = "stage";
    public const string AtsKind = "ats_kind";
    public const string Tier = "tier";
    public const string Environment = "environment";
    public const string Outcome = "outcome";

    public static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>(StringComparer.Ordinal) { Stage, AtsKind, Tier, Environment, Outcome };

    /// <summary>True when <paramref name="label"/> is an allowed metric label.</summary>
    public static bool IsAllowed(string label) => Allowed.Contains(label);
}
