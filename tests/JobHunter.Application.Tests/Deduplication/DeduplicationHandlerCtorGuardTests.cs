using JobHunter.Application.Deduplication;
using JobHunter.Application.Normalization;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Deduplication;

/// <summary>
/// The dependency-guard arm of <see cref="DeduplicationHandler"/>: every injected collaborator is null-checked
/// in the field initialiser, so a missing dependency fails fast at construction rather than at first message.
/// </summary>
public sealed class DeduplicationHandlerCtorGuardTests
{
    private readonly IRawPostingReader _rawPostings = Substitute.For<IRawPostingReader>();
    private readonly IJobSourceRepository _sources = Substitute.For<IJobSourceRepository>();
    private readonly ICompanyRepository _companies = Substitute.For<ICompanyRepository>();
    private readonly IPostingNormalizerCatalog _normalizers = Substitute.For<IPostingNormalizerCatalog>();
    private readonly TechnologyTagger _technologyTagger = new(new TechnologyVocabulary([]));
    private readonly IJobRepository _jobs = Substitute.For<IJobRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var logger = NullLogger<DeduplicationHandler>.Instance;

        Should.Throw<ArgumentNullException>(() => new DeduplicationHandler(null!, _sources, _companies, _normalizers, _technologyTagger, _jobs, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new DeduplicationHandler(_rawPostings, null!, _companies, _normalizers, _technologyTagger, _jobs, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new DeduplicationHandler(_rawPostings, _sources, null!, _normalizers, _technologyTagger, _jobs, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new DeduplicationHandler(_rawPostings, _sources, _companies, null!, _technologyTagger, _jobs, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new DeduplicationHandler(_rawPostings, _sources, _companies, _normalizers, null!, _jobs, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new DeduplicationHandler(_rawPostings, _sources, _companies, _normalizers, _technologyTagger, null!, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new DeduplicationHandler(_rawPostings, _sources, _companies, _normalizers, _technologyTagger, _jobs, null!, logger));
        Should.Throw<ArgumentNullException>(() => new DeduplicationHandler(_rawPostings, _sources, _companies, _normalizers, _technologyTagger, _jobs, _clock, null!));
    }
}
