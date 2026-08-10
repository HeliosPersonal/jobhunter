using JobHunter.Application.Discovery;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Discovery;

/// <summary>
/// The dependency-guard arm of <see cref="RedetectBindingHandler"/>: every injected collaborator is
/// null-checked in the field initialiser, so a missing dependency fails fast at construction.
/// </summary>
public sealed class RedetectBindingHandlerCtorGuardTests
{
    private readonly IRedetectionQuery _dueCandidates = Substitute.For<IRedetectionQuery>();
    private readonly ICompanyRepository _companies = Substitute.For<ICompanyRepository>();
    private readonly IJobSourceRepository _sources = Substitute.For<IJobSourceRepository>();
    private readonly IBindingDetector _detector = Substitute.For<IBindingDetector>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly IClock _clock = Substitute.For<IClock>();

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var logger = NullLogger<RedetectBindingHandler>.Instance;

        Should.Throw<ArgumentNullException>(() => new RedetectBindingHandler(null!, _companies, _sources, _detector, _ids, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new RedetectBindingHandler(_dueCandidates, null!, _sources, _detector, _ids, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new RedetectBindingHandler(_dueCandidates, _companies, null!, _detector, _ids, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new RedetectBindingHandler(_dueCandidates, _companies, _sources, null!, _ids, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new RedetectBindingHandler(_dueCandidates, _companies, _sources, _detector, null!, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new RedetectBindingHandler(_dueCandidates, _companies, _sources, _detector, _ids, null!, logger));
        Should.Throw<ArgumentNullException>(() => new RedetectBindingHandler(_dueCandidates, _companies, _sources, _detector, _ids, _clock, null!));
    }
}
