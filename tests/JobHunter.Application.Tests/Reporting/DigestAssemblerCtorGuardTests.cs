using JobHunter.Application.Reporting;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Reporting;

/// <summary>
/// The dependency-guard arm of <see cref="DigestAssembler"/>: every injected collaborator, the digest options
/// and the apply-verification options are null-checked in the field initialiser, so a missing dependency fails
/// fast at construction rather than mid-assembly.
/// </summary>
public sealed class DigestAssemblerCtorGuardTests
{
    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly IDigestScopeQuery _scope = Substitute.For<IDigestScopeQuery>();
    private readonly IDegradedCoverageQuery _degraded = Substitute.For<IDegradedCoverageQuery>();
    private readonly IActiveCompanyCountQuery _activeCompanies = Substitute.For<IActiveCompanyCountQuery>();
    private readonly IDigestRepository _digests = Substitute.For<IDigestRepository>();
    private readonly IApplyLinkVerifier _applyLinkVerifier = Substitute.For<IApplyLinkVerifier>();
    private readonly INarrativeSynthesizer _narrativeSynthesizer = Substitute.For<INarrativeSynthesizer>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly DigestOptions _options = new();
    private readonly ApplyVerificationOptions _applyVerification = new();
    private readonly ILearningSwitch _learning = Substitute.For<ILearningSwitch>();
    private readonly IClock _clock = Substitute.For<IClock>();

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var logger = NullLogger<DigestAssembler>.Instance;

        Should.Throw<ArgumentNullException>(() => new DigestAssembler(null!, _scope, _degraded, _activeCompanies, _digests, _applyLinkVerifier, _narrativeSynthesizer, _ids, _options, _applyVerification, _learning, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new DigestAssembler(_runs, null!, _degraded, _activeCompanies, _digests, _applyLinkVerifier, _narrativeSynthesizer, _ids, _options, _applyVerification, _learning, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new DigestAssembler(_runs, _scope, null!, _activeCompanies, _digests, _applyLinkVerifier, _narrativeSynthesizer, _ids, _options, _applyVerification, _learning, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new DigestAssembler(_runs, _scope, _degraded, null!, _digests, _applyLinkVerifier, _narrativeSynthesizer, _ids, _options, _applyVerification, _learning, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new DigestAssembler(_runs, _scope, _degraded, _activeCompanies, null!, _applyLinkVerifier, _narrativeSynthesizer, _ids, _options, _applyVerification, _learning, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new DigestAssembler(_runs, _scope, _degraded, _activeCompanies, _digests, null!, _narrativeSynthesizer, _ids, _options, _applyVerification, _learning, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new DigestAssembler(_runs, _scope, _degraded, _activeCompanies, _digests, _applyLinkVerifier, null!, _ids, _options, _applyVerification, _learning, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new DigestAssembler(_runs, _scope, _degraded, _activeCompanies, _digests, _applyLinkVerifier, _narrativeSynthesizer, null!, _options, _applyVerification, _learning, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new DigestAssembler(_runs, _scope, _degraded, _activeCompanies, _digests, _applyLinkVerifier, _narrativeSynthesizer, _ids, null!, _applyVerification, _learning, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new DigestAssembler(_runs, _scope, _degraded, _activeCompanies, _digests, _applyLinkVerifier, _narrativeSynthesizer, _ids, _options, null!, _learning, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new DigestAssembler(_runs, _scope, _degraded, _activeCompanies, _digests, _applyLinkVerifier, _narrativeSynthesizer, _ids, _options, _applyVerification, null!, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new DigestAssembler(_runs, _scope, _degraded, _activeCompanies, _digests, _applyLinkVerifier, _narrativeSynthesizer, _ids, _options, _applyVerification, _learning, null!, logger));
        Should.Throw<ArgumentNullException>(() => new DigestAssembler(_runs, _scope, _degraded, _activeCompanies, _digests, _applyLinkVerifier, _narrativeSynthesizer, _ids, _options, _applyVerification, _learning, _clock, null!));
    }
}
