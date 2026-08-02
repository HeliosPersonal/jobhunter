---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
stage: "06-07"
ticket: ""
tags: [engineering, standards, jobhunter]
---

# Coding standards

> Enforced by the compiler where possible, by architecture tests where not, by review where neither.
> If a rule here is not enforced by one of those three, it is a suggestion and marked as such.

---

## 1. Compiler-enforced baseline

`Directory.Build.props` at the repository root:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
```

`Directory.Packages.props` uses Central Package Management with
`CentralPackageTransitivePinningEnabled` — a transitive package with a CVE is pinned in one place.

There is **no suppression file**. A warning is either fixed or, in the rare justified case,
suppressed inline with `#pragma warning disable` *and* a comment stating why.

---

## 2. Architecture rules

Enforced by `JobHunter.ArchitectureTests` (F0 T12), which fails the build, not the reviewer's patience.

**This table is the single canonical enumeration of the architecture rules — there are exactly
8 architecture rules.** Every other site (F0 T12, F0 epic, F0 test-plan, readiness G5) references
this list and states **8**; none re-enumerates them.

| # | Rule | Rationale |
|---|---|---|
| 1 | `JobHunter.Domain` references no package except `Microsoft.Extensions.*.Abstractions` | The domain must be testable and portable; a domain that needs EF Core is not a domain |
| 2 | Dependency direction is `Hosts → Infrastructure/Claude/Scrapers/Search → Application → Domain` | [[../00-overview/sad]] §5 |
| 3 | `JobHunter.Contracts` references nothing | Events must be consumable by any host without dragging a dependency graph |
| 4 | No type in `Persistence/Queries/` calls `ExecuteAsync` or `Execute` | Dapper never writes ([[../00-overview/adr/0003-postgresql-efcore-dapper\|ADR-0003]]) |
| 5 | No use of `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now` outside `SystemClock` | Time must be injectable or nothing time-dependent is testable |
| 6 | Nothing outside `src/Aspire/` references `JobHunter.AppHost` | [[../00-overview/adr/0013-aspire-local-dev-only\|ADR-0013]] |
| 7 | Every `IEntityTypeConfiguration<T>` is `internal sealed` | Configuration is not API |
| 8 | No `public` type in `Infrastructure` that is not an extension method or an options class | Adapters are reached through ports, not directly |

---

## 3. Structure

**One `DependencyInjection.cs` per project**, at its root, exposing exactly one extension method:

```csharp
public static class DependencyInjection
{
    public static IServiceCollection AddJobHunterInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AnthropicOptions>()
            .Bind(configuration.GetSection(AnthropicOptions.SectionName))
            .Validate(o => o.IsValid(out _), "Anthropic options are invalid")
            .ValidateOnStart();
        // ...
        return services;
    }
}
```

Marked `[ExcludeFromCodeCoverage]` — wiring is verified by the system starting, not by a unit test.

**Options validate at startup, never at first use.** A missing API key must fail the pod's readiness
probe at 02:00:01, not silently at 02:14 when the batch is submitted.

**Folder per concept, not per pattern.** `Application/Enrichment/` holds the handler, its request,
its result and its service — not `Handlers/`, `Requests/`, `Services/` split three ways.

---

## 4. Errors

**Expected business outcomes are values. Unexpected states are exceptions.**

```csharp
public enum AtsDetectionOutcome { Detected, NoBoardFound, Ambiguous, HostUnreachable, RateLimited }

public sealed record AtsDetectionResult(AtsDetectionOutcome Outcome, AtsBinding? Binding, string? Detail)
{
    public static AtsDetectionResult Detected(AtsBinding b) => new(AtsDetectionOutcome.Detected, b, null);
    public static AtsDetectionResult NoBoardFound(string domain) =>
        new(AtsDetectionOutcome.NoBoardFound, null, $"No ATS board discovered for {domain}");
}
```

`ArgumentException`, `ArgumentNullException` and `InvalidOperationException` signal programmer error.
`HttpRequestException`, `NpgsqlException` and their kin are infrastructure faults, handled by
resilience policies. Neither is used for control flow. A `catch` with an empty body fails review,
always.

---

## 5. Domain modelling

- **Value objects validate themselves** and expose both patterns:
  `Fingerprint.TryCreate(...)` returns `bool`; `Fingerprint.Create(...)` throws. Callers that can
  handle invalidity use `Try`; callers that cannot use the throwing form and mean it.
- **Records for immutable data, classes for aggregates with behaviour.** An aggregate exposes
  methods that enforce invariants; it does not expose settable collections.
- **Enums persist as `text`**, never as ordinals ([[../architecture/data-model]] §5).
- **No primitive obsession on the things that matter**: `SalaryRange`, `Fingerprint`,
  `CanonicalDomain`, `TimezoneBand` are types. `title` is a `string` — not everything needs a wrapper.

---

## 6. Async

- `async`/`await` everywhere on the I/O path; no `.Result`, no `.Wait()`, no `.GetAwaiter().GetResult()`.
- Every async method takes a `CancellationToken` and passes it on. A pipeline that cannot be
  cancelled cannot be shut down gracefully.
- `ConfigureAwait` is not used — there is no synchronisation context in ASP.NET Core or a Worker.
- Bounded parallelism via `Parallel.ForEachAsync` with an explicit `MaxDegreeOfParallelism`.
  Unbounded fan-out over 300 companies is how a shared Postgres runs out of connections.

---

## 7. Logging

```csharp
// Yes — structured, correlated, no interpolation
logger.LogInformation("Discovered {JobCount} jobs from {AtsKind} for {CompanyDomain}",
    jobs.Count, binding.AtsKind, company.CanonicalDomain);

// No — unsearchable, and the CV is now in Loki forever
logger.LogInformation($"Matched job against CV: {cvText}");
```

- `run_id` and `job_id` on every pipeline log, via `ILogger.BeginScope`.
- **Never logged:** CV text, prompt bodies, model responses, API keys, connection strings, Telegram
  tokens. Enforced by `SecretRedactionTests` and a scrubbing processor (gate G6).
- `LogError` means a human must look. Everything else is `LogInformation` or below. A log that fires
  every run is not an error.

---

## 8. Naming

| Thing | Convention | Example |
|---|---|---|
| Events | `PascalCase`, past tense | `EnrichmentCompleted` |
| Handlers | `<Event>Handler` or `<Stage>Handler` | `DeduplicationHandler` |
| Ports | `I<Noun>` in `Domain/Abstractions` | `IJobSource`, `ILlmBatchClient` |
| Adapters | `<Provider><Port>` | `GreenhouseJobSource`, `AnthropicBatchClient` |
| Dapper queries | `<Intent>Query` | `DigestProjectionQuery` |
| EF configurations | `<Entity>Configuration` | `JobConfiguration` |
| Options | `<Adapter>Options` with `const string SectionName` | `AnthropicOptions` |
| Tests | `Method_Scenario_ExpectedOutcome` | `Fingerprint_SameTitleDifferentCase_IsEqual` |
| Migrations | `<Feature>_<What>` | `F2_AddJobsAndAliases` |

Tables and columns are `snake_case`; C# is `PascalCase`; the mapping is explicit in configurations,
never conventional.

---

## 9. Git

- Branches: `feature/{FEATURE}-{TASK}-{kebab-description}` — `feature/F3-T05-anthropic-batch-client`.
- Conventional commit subjects: `feat(f3): submit enrichment batch with cost pre-check`.
- **No AI attribution trailers.**
- One task, one PR, ≤ 500 LOC, ≤ 1 day (gate G8).
- The PR description links the task file and states which acceptance criteria it satisfies.

---

## Related

- [[testing-strategy]] · [[../IMPLEMENTATION-READINESS]] §2 · [[../00-overview/sad]] §8
