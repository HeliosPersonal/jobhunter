using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobHunter.Scrapers.Detection;

/// <summary>The shape of a binding's evidence document: the ordered probe trail behind the decision.</summary>
public sealed record DetectionEvidence(string ProbedAt, IReadOnlyList<ProbeCandidate> Candidates);

/// <summary>
/// How a binding's evidence document is written: camelCase keys, and the <see cref="Domain.Companies.AtsKind"/>
/// as text, never an ordinal (coding-standards §enums persist as text). The document is stored verbatim in
/// <c>ats_bindings.evidence</c> so a wrong binding is explainable without re-running detection.
/// </summary>
internal static class DetectionEvidenceJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
