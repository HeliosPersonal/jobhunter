using System.Text.Json.Serialization;

namespace JobHunter.Contracts.Pipeline;

/// <summary>
/// The source-generated JSON context for the pipeline integration events (event-catalog §4): no
/// reflection at runtime and a compile-time error on an unserialisable member. Every event that crosses
/// the bus is declared here.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SourceFetchRequested))]
[JsonSerializable(typeof(RawPostingIngested))]
[JsonSerializable(typeof(SourceQuarantined))]
[JsonSerializable(typeof(JobClosed))]
public sealed partial class PipelineEventContext : JsonSerializerContext;
