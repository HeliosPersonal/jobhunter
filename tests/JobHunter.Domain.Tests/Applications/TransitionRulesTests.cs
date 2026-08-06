using JobHunter.Domain.Applications;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Applications;

/// <summary>
/// T01: the transition table. A table can be enumerated, which is how all 49 pairs
/// (7 statuses × 7, including the diagonal and every <c>→ New</c> cell) are covered against
/// [[../contracts/application-api]] rather than the handful someone thought of (SAD §5, ADR-F6-0001).
/// </summary>
public sealed class TransitionRulesTests
{
    // The contract's §Transition matrix, transcribed. Rows are the current status, columns the target.
    // true = permitted (✓), false = refused (—).
    private static readonly Dictionary<ApplicationStatus, Dictionary<ApplicationStatus, bool>> Matrix =
        new()
        {
            //                                New,   Saved,  Applied, Interview, Rejected, Offer, Ignored
            [ApplicationStatus.New] = Row(New: true, Saved: true, Applied: true, Interview: true, Rejected: true, Offer: false, Ignored: true),
            [ApplicationStatus.Saved] = Row(New: false, Saved: true, Applied: true, Interview: true, Rejected: true, Offer: false, Ignored: true),
            [ApplicationStatus.Applied] = Row(New: false, Saved: true, Applied: true, Interview: true, Rejected: true, Offer: true, Ignored: true),
            [ApplicationStatus.Interview] = Row(New: false, Saved: false, Applied: false, Interview: true, Rejected: true, Offer: true, Ignored: true),
            [ApplicationStatus.Rejected] = Row(New: false, Saved: false, Applied: true, Interview: false, Rejected: true, Offer: false, Ignored: true),
            [ApplicationStatus.Offer] = Row(New: false, Saved: false, Applied: false, Interview: false, Rejected: true, Offer: true, Ignored: false),
            [ApplicationStatus.Ignored] = Row(New: false, Saved: true, Applied: true, Interview: false, Rejected: false, Offer: false, Ignored: true),
        };

    public static TheoryData<ApplicationStatus, ApplicationStatus> AllPairs()
    {
        var data = new TheoryData<ApplicationStatus, ApplicationStatus>();
        foreach (var from in Enum.GetValues<ApplicationStatus>())
        {
            foreach (var to in Enum.GetValues<ApplicationStatus>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Evaluate_matches_the_contract_matrix_for_every_pair(ApplicationStatus from, ApplicationStatus to)
    {
        var expectedPermitted = Matrix[from][to];

        var result = TransitionRules.Evaluate(from, to);

        result.IsPermitted.ShouldBe(
            expectedPermitted,
            $"{from} → {to} should be {(expectedPermitted ? "permitted" : "refused")} per the contract matrix.");
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Every_refusal_carries_a_remedy_and_every_permission_carries_none(ApplicationStatus from, ApplicationStatus to)
    {
        var result = TransitionRules.Evaluate(from, to);

        if (result.IsPermitted)
        {
            result.Remedy.ShouldBeNull();
        }
        else
        {
            // A refusal without a remedy is just an obstacle (ADR-F6-0001).
            result.Remedy.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void The_matrix_covers_all_forty_nine_pairs()
    {
        var pairCount = Matrix.Sum(row => row.Value.Count);
        pairCount.ShouldBe(49);
    }

    [Fact]
    public void The_diagonal_is_always_a_permitted_no_op()
    {
        foreach (var status in Enum.GetValues<ApplicationStatus>())
        {
            TransitionRules.Evaluate(status, status).IsPermitted.ShouldBeTrue(
                $"{status} → {status} is a legal idempotent no-op.");
        }
    }

    private static Dictionary<ApplicationStatus, bool> Row(
        bool New,
        bool Saved,
        bool Applied,
        bool Interview,
        bool Rejected,
        bool Offer,
        bool Ignored) =>
        new()
        {
            [ApplicationStatus.New] = New,
            [ApplicationStatus.Saved] = Saved,
            [ApplicationStatus.Applied] = Applied,
            [ApplicationStatus.Interview] = Interview,
            [ApplicationStatus.Rejected] = Rejected,
            [ApplicationStatus.Offer] = Offer,
            [ApplicationStatus.Ignored] = Ignored,
        };
}
