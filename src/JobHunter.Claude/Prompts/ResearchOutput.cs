using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Research;

namespace JobHunter.Claude.Prompts;

/// <summary>
/// The structured output of one research-synthesis item (research-schema §Output record), parsed from the
/// model's tool-use payload before any verification. The <see cref="Summary"/> is itself constrained to
/// cited claims; each <see cref="ClaimDto"/> carries the <see cref="ClaimDto.SourceUrl"/> the model asserts
/// supports it — which the verifier (T07) checks by set membership against the fetched source URLs before a
/// single claim is stored. Nothing here is trusted yet: the schema can require a URL to be present, not to
/// be true.
///
/// <para><see cref="Stage"/> and <see cref="EmployeeBand"/> are the optional firmographic feedback (AC-10):
/// present only when a document supported the classification, absent otherwise — never guessed. The
/// orchestrator (T08) feeds them back to the <c>Company</c> aggregate.</para>
/// </summary>
public sealed record ResearchOutput(
    string Summary,
    IReadOnlyList<ClaimDto> Claims,
    CompanyStage? Stage = null,
    string? EmployeeBand = null);

/// <summary>
/// One asserted claim from the synthesiser. <see cref="SourceUrl"/> must be one of the URLs supplied in the
/// prompt — an unverified assertion at this point, discarded by the verifier if it matches no fetched
/// document (invariant 5). <see cref="IsWarning"/> flags a layoff, a down round or a credible organisational
/// problem so the presentation can surface it.
/// </summary>
public sealed record ClaimDto(
    ResearchCategory Category,
    string Claim,
    string SourceUrl,
    bool IsWarning);
