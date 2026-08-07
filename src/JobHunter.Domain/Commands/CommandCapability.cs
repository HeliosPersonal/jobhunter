namespace JobHunter.Domain.Commands;

/// <summary>
/// A command's sensitivity, not a role: the Owner is the sole principal (invariant 9). It gates
/// whether a command's confirmation and operator framing apply, never <em>who</em> may run it
/// ([[command-catalogue]] legend, ADR-F10-0001). <see cref="Unspecified"/> is the default enum value on
/// purpose — a descriptor that forgets to declare a capability fails closed at construction rather than
/// silently defaulting to an everyday command (T01, QG-2).
/// </summary>
public enum CommandCapability
{
    /// <summary>No capability declared. Never valid on a constructed descriptor — the guard rejects it.</summary>
    Unspecified = 0,

    /// <summary>An everyday read or action.</summary>
    Standard = 1,

    /// <summary>Touches system state or is destructive; framed as an operator action in the surface.</summary>
    Sensitive = 2,
}
