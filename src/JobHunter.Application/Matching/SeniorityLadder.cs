using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Matching;

/// <summary>
/// The individual-contributor rungs the seniority-floor rule compares on (ADR-F4-0003). The
/// <see cref="Seniority"/> enum mixes the IC ladder with off-ladder roles (<see cref="Seniority.Lead"/>,
/// <see cref="Seniority.Manager"/>) whose ordinal position carries no "how many levels below" meaning, so the
/// filter never trusts <c>(int)</c> on the enum: a level is comparable only when it maps to a rung here.
///
/// <para>The mapping is deliberately partial. Junior → Mid → Senior → Staff → Principal is a monotone ladder
/// where "two levels below" is a fact; Lead and Manager are parallel tracks, not higher rungs, so they return
/// <c>null</c> and the seniority-floor rule declines to fire rather than invent a comparison (test-plan hard
/// case: a management title is never excluded as "too junior").</para>
/// </summary>
public static class SeniorityLadder
{
    /// <summary>
    /// The IC rung of <paramref name="seniority"/>, or <c>null</c> for an off-ladder level that cannot be
    /// compared by height. Rungs are contiguous from 0 so a difference is a level count.
    /// </summary>
    public static int? Rung(Seniority seniority) => seniority switch
    {
        Seniority.Junior => 0,
        Seniority.Mid => 1,
        Seniority.Senior => 2,
        Seniority.Staff => 3,
        Seniority.Principal => 4,
        _ => null,
    };
}
