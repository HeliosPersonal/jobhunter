namespace JobHunter.Domain.Intelligence;

/// <summary>
/// What the role actually <em>is</em>, classified from the work the posting describes rather than from
/// the title string (enrichment-schema §prompt, TUNE-03). This is the structured signal the F4
/// <c>alignment</c> component and the F7 preference dimensions act on: it encodes the Owner's Tier-1/2/3
/// target trajectory so a "Senior Software Engineer" doing platform work and a "Platform Engineer" doing
/// the same work land in the same family. Persisted as <c>text</c>, never an ordinal (coding-standards §5).
///
/// <para>Unlike the other enrichment enums, <see cref="Other"/> is a real classification — an honest
/// "none of the above" — not a parse sentinel. It is therefore part of the generated wire schema, and the
/// tolerant parser lands an unrecognised or absent value on it (parsing step 8).</para>
/// </summary>
public enum RoleFamily
{
    /// <summary>Building the platform/infrastructure that AI systems run on (inference, serving, eval infra).</summary>
    AiPlatform,

    /// <summary>General platform/infrastructure engineering not centred on AI (internal platforms, infra, tooling).</summary>
    Platform,

    /// <summary>Building product features on top of AI systems (LLM-backed application features).</summary>
    AiApplications,

    /// <summary>Forward-deployed / solutions engineering embedded with customers.</summary>
    ForwardDeployed,

    /// <summary>Founding engineer at an early-stage company — broad ownership across the stack.</summary>
    FoundingEng,

    /// <summary>Backend engineering without a platform or AI centre of gravity.</summary>
    BackendGeneric,

    /// <summary>Frontend / client engineering.</summary>
    Frontend,

    /// <summary>Fullstack engineering spanning frontend and backend.</summary>
    Fullstack,

    /// <summary>DevOps / SRE — reliability, operations, delivery.</summary>
    DevOpsSRE,

    /// <summary>Machine-learning research or applied research.</summary>
    MlResearch,

    /// <summary>Data science / analytics engineering.</summary>
    DataScience,

    /// <summary>Prompt engineering as the primary discipline of the role.</summary>
    PromptEng,

    /// <summary>Enterprise CRUD / line-of-business application work — the guard case for an AI-branded title
    /// whose described work is ordinary CRUD.</summary>
    EnterpriseCrud,

    /// <summary>An honest "none of the above" — a real classification and the fallback for an unrecognised
    /// or absent value (parsing step 8), not merely a sentinel.</summary>
    Other,
}
