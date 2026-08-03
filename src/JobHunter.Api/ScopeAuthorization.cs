using System.Security.Claims;

namespace JobHunter.Api;

/// <summary>
/// The pure authorisation predicates the read and admin policies are built from (ADR-0014). Kept apart
/// from the host wiring in <see cref="ApiSecurityExtensions"/> so the scope-plus-Owner rule — the one
/// that makes a valid token for a different subject a 403, not a 200 — is unit-tested directly rather
/// than only through a running host.
/// </summary>
internal static class ScopeAuthorization
{
    /// <summary>
    /// True when the principal carries <paramref name="requiredScope"/> in its space-delimited Keycloak
    /// <c>scope</c> claim. Absent or blank claim is false — scope is never assumed.
    /// </summary>
    internal static bool HasScope(ClaimsPrincipal user, string requiredScope)
    {
        ArgumentNullException.ThrowIfNull(user);

        var scopeClaim = user.FindFirst("scope")?.Value;
        return scopeClaim is not null
               && scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(requiredScope);
    }

    /// <summary>
    /// True when the principal's subject equals the configured Owner (invariant 9). A blank
    /// <paramref name="ownerSubject"/> — local development with no Owner configured — disables the check
    /// so the dev token is admitted; every deployed environment configures it and the check is enforced.
    /// </summary>
    internal static bool IsOwner(ClaimsPrincipal user, string? ownerSubject)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrWhiteSpace(ownerSubject))
        {
            return true;
        }

        var subject = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
        return string.Equals(subject, ownerSubject, StringComparison.Ordinal);
    }

    /// <summary>
    /// The full gate applied by both policies: the required scope <em>and</em> the Owner subject. Scope
    /// alone never grants access (security §2).
    /// </summary>
    internal static bool Satisfies(ClaimsPrincipal user, string requiredScope, string? ownerSubject) =>
        HasScope(user, requiredScope) && IsOwner(user, ownerSubject);
}
