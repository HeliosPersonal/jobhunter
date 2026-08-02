namespace JobHunter.Domain.Common;

/// <summary>
/// A machine-readable failure reason. A <see cref="Result{T}"/> failure always carries one, so a
/// failure-without-reason is unrepresentable (coding-standards §4).
/// </summary>
public sealed record Error(string Code, string Message)
{
    /// <summary>Sentinel used to make "no error" explicit on the success path.</summary>
    public static readonly Error None = new(string.Empty, string.Empty);
}
