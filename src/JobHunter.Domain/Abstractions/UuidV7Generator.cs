namespace JobHunter.Domain.Abstractions;

/// <summary>
/// Production id generator. <see cref="Guid.CreateVersion7()"/> embeds a Unix-millisecond timestamp,
/// so ids drawn in sequence sort in creation order — the property asserted over 10 000 draws in T02.
/// </summary>
public sealed class UuidV7Generator : IIdGenerator
{
    public Guid NewId() => Guid.CreateVersion7();
}
