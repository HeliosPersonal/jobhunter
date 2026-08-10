using JobHunter.Application.Discovery;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Discovery;

/// <summary>
/// The dependency-guard arm of <see cref="FetchSourceHandler"/>: every injected port is null-checked in the
/// field initialiser, so a missing collaborator fails fast at construction.
/// </summary>
public sealed class FetchSourceHandlerBranchTests
{
    private readonly IJobSourceRepository _sources = Substitute.For<IJobSourceRepository>();
    private readonly ICompanyRepository _companies = Substitute.For<ICompanyRepository>();
    private readonly IJobSourceCatalog _catalog = Substitute.For<IJobSourceCatalog>();
    private readonly IRawPostingRepository _rawPostings = Substitute.For<IRawPostingRepository>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly IClock _clock = Substitute.For<IClock>();

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var logger = NullLogger<FetchSourceHandler>.Instance;

        Should.Throw<ArgumentNullException>(() => new FetchSourceHandler(null!, _companies, _catalog, _rawPostings, _ids, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new FetchSourceHandler(_sources, null!, _catalog, _rawPostings, _ids, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new FetchSourceHandler(_sources, _companies, null!, _rawPostings, _ids, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new FetchSourceHandler(_sources, _companies, _catalog, null!, _ids, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new FetchSourceHandler(_sources, _companies, _catalog, _rawPostings, null!, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new FetchSourceHandler(_sources, _companies, _catalog, _rawPostings, _ids, null!, logger));
        Should.Throw<ArgumentNullException>(() => new FetchSourceHandler(_sources, _companies, _catalog, _rawPostings, _ids, _clock, null!));
    }
}
