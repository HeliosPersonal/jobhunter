using JobHunter.Domain.Common;

namespace JobHunter.Domain.Intelligence;

/// <summary>
/// The resolving sub-signals that sit alongside the single <see cref="AiUsageLevel"/> scalar
/// (enrichment-schema §Output record, TUNE-04). The scalar alone cannot separate "builds the platform AI
/// runs on" from "the team uses Copilot"; these four booleans do, each derived from the <em>engineering
/// work the posting describes</em> — never from what the company sells. They sharpen the target/trap
/// boundary the career-alignment review flags: a role that merely <see cref="UsesAiTooling"/> is not
/// confused with one that <see cref="BuildsAiProduct"/> or <see cref="BuildsAiInfra"/>.
///
/// <para>The value object is always present (never null) — an unrecognised or absent wire value degrades
/// to <see cref="None"/>, all-false, rather than throwing (parsing step 8). It is an owned type on the
/// <see cref="Enrichment"/> aggregate, persisted as four <c>boolean</c> columns.</para>
/// </summary>
public sealed class AiSignals : ValueObject
{
    /// <summary>All-false: the tolerant landing place when the posting shows no AI engineering signal.</summary>
    public static readonly AiSignals None = new(false, false, false, false);

    public AiSignals(bool buildsAiProduct, bool buildsAiInfra, bool usesAiTooling, bool isResearch)
    {
        BuildsAiProduct = buildsAiProduct;
        BuildsAiInfra = buildsAiInfra;
        UsesAiTooling = usesAiTooling;
        IsResearch = isResearch;
    }

    private AiSignals()
    {
    }

    /// <summary>The role builds AI-facing product features on top of models (e.g. an LLM-backed feature).</summary>
    public bool BuildsAiProduct { get; private set; }

    /// <summary>The role builds the platform/infrastructure AI runs on (serving, inference, training systems).</summary>
    public bool BuildsAiInfra { get; private set; }

    /// <summary>The role merely uses AI tooling to do otherwise-conventional work (e.g. Copilot). The trap side.</summary>
    public bool UsesAiTooling { get; private set; }

    /// <summary>The role is applied/ML research — training or evaluating models as the substance of the work.</summary>
    public bool IsResearch { get; private set; }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return BuildsAiProduct;
        yield return BuildsAiInfra;
        yield return UsesAiTooling;
        yield return IsResearch;
    }
}
