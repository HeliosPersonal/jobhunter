namespace JobHunter.Domain.Commands;

/// <summary>
/// The section a command belongs to in the grouped <c>/help</c> and <c>/start</c> lists — the same
/// six headings, in the same order, as <c>contracts/command-catalogue.md</c> (SAD §5). The grouping is
/// intrinsic command metadata, declared once on the descriptor so the help sections derive from the
/// single source and cannot drift. <see cref="Unspecified"/> is the default enum value on purpose: a
/// descriptor that forgets its group fails closed at construction rather than landing in an unnamed
/// section (T10, QG-2), mirroring <see cref="CommandCapability.Unspecified"/>.
/// </summary>
public enum CommandGroup
{
    /// <summary>No group declared. Never valid on a constructed descriptor — the guard rejects it.</summary>
    Unspecified = 0,

    /// <summary>Digest and discovery: <c>/digest</c>, <c>/more</c>, <c>/search</c>, <c>/hidden</c>.</summary>
    DigestAndDiscovery = 1,

    /// <summary>Pipeline: <c>/saved</c>, <c>/pipeline</c>, <c>/due</c>, <c>/note</c>, <c>/stats</c>.</summary>
    Pipeline = 2,

    /// <summary>Company: <c>/company</c>, <c>/research</c>.</summary>
    Company = 3,

    /// <summary>Profile and preferences: <c>/cv</c>, <c>/prefs</c>, <c>/forget</c>, <c>/floor</c>.</summary>
    ProfileAndPreferences = 4,

    /// <summary>Operations: <c>/status</c>, <c>/cost</c>, <c>/sources</c>, <c>/run</c>, <c>/redeliver</c>.</summary>
    Operations = 5,

    /// <summary>Meta: <c>/start</c>, <c>/help</c>, <c>/cancel</c>.</summary>
    Meta = 6,
}
