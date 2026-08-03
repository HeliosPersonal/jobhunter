using System.Security.Claims;
using JobHunter.Api;
using Shouldly;
using Xunit;

namespace JobHunter.Api.Tests;

/// <summary>
/// The scope-plus-Owner gate both policies are built from (ADR-0014, security §2). The behaviour that
/// matters: scope alone never admits — a valid <c>jobhunter:read</c> token for a subject other than the
/// Owner is refused, which at the endpoint becomes a 403 (AC — "a valid token for a subject other than
/// the Owner is refused").
/// </summary>
public sealed class ScopeAuthorizationTests
{
    private const string Owner = "owner-subject-123";

    private static ClaimsPrincipal Principal(string? scope = null, string? subject = null)
    {
        var claims = new List<Claim>();
        if (scope is not null)
        {
            claims.Add(new Claim("scope", scope));
        }

        if (subject is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, subject));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public void A_present_scope_in_the_space_delimited_claim_is_recognised()
    {
        var user = Principal(scope: "jobhunter:read jobhunter:admin");

        ScopeAuthorization.HasScope(user, "jobhunter:admin").ShouldBeTrue();
        ScopeAuthorization.HasScope(user, "jobhunter:read").ShouldBeTrue();
    }

    [Fact]
    public void A_missing_scope_claim_is_never_assumed()
    {
        var user = Principal(subject: Owner);

        ScopeAuthorization.HasScope(user, "jobhunter:read").ShouldBeFalse();
    }

    [Fact]
    public void A_scope_claim_that_lacks_the_required_scope_is_refused()
    {
        var user = Principal(scope: "jobhunter:read");

        ScopeAuthorization.HasScope(user, "jobhunter:admin").ShouldBeFalse();
    }

    [Fact]
    public void The_owner_check_matches_the_subject_from_the_name_identifier_claim()
    {
        var user = Principal(subject: Owner);

        ScopeAuthorization.IsOwner(user, Owner).ShouldBeTrue();
    }

    [Fact]
    public void The_owner_check_reads_the_subject_from_the_sub_claim_when_no_name_identifier()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Owner)], "test"));

        ScopeAuthorization.IsOwner(user, Owner).ShouldBeTrue();
    }

    [Fact]
    public void A_token_for_a_different_subject_is_not_the_owner()
    {
        var user = Principal(subject: "someone-else");

        ScopeAuthorization.IsOwner(user, Owner).ShouldBeFalse();
    }

    [Fact]
    public void A_blank_configured_owner_disables_the_subject_check_for_local_development()
    {
        var user = Principal(subject: "anyone");

        ScopeAuthorization.IsOwner(user, string.Empty).ShouldBeTrue();
        ScopeAuthorization.IsOwner(user, null).ShouldBeTrue();
        ScopeAuthorization.IsOwner(user, "   ").ShouldBeTrue();
    }

    [Fact]
    public void Scope_alone_never_admits_the_owner_subject_is_also_required()
    {
        // A valid read token, but for a different realm subject — refused (security §2).
        var wrongSubject = Principal(scope: "jobhunter:read", subject: "someone-else");

        ScopeAuthorization.Satisfies(wrongSubject, "jobhunter:read", Owner).ShouldBeFalse();
    }

    [Fact]
    public void The_owner_subject_alone_never_admits_the_scope_is_also_required()
    {
        var noScope = Principal(subject: Owner);

        ScopeAuthorization.Satisfies(noScope, "jobhunter:read", Owner).ShouldBeFalse();
    }

    [Fact]
    public void Both_the_required_scope_and_the_owner_subject_admit()
    {
        var owner = Principal(scope: "jobhunter:read", subject: Owner);

        ScopeAuthorization.Satisfies(owner, "jobhunter:read", Owner).ShouldBeTrue();
    }
}
