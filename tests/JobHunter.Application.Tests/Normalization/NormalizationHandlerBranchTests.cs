using JobHunter.Application.Normalization;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Normalization;

/// <summary>
/// The dependency-guard arm of <see cref="NormalizationHandler"/>: every injected collaborator is null-checked
/// in the field initialiser, so a missing dependency fails fast at construction rather than at first message.
/// </summary>
public sealed class NormalizationHandlerBranchTests
{
    private readonly IRawPostingReader _rawPostings = Substitute.For<IRawPostingReader>();
    private readonly IJobSourceRepository _sources = Substitute.For<IJobSourceRepository>();
    private readonly ICompanyRepository _companies = Substitute.For<ICompanyRepository>();
    private readonly IPostingNormalizerCatalog _normalizers = Substitute.For<IPostingNormalizerCatalog>();
    private readonly TechnologyTagger _technologyTagger = new(new TechnologyVocabulary([]));
    private readonly IClock _clock = Substitute.For<IClock>();

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var logger = NullLogger<NormalizationHandler>.Instance;

        Should.Throw<ArgumentNullException>(() => new NormalizationHandler(null!, _sources, _companies, _normalizers, _technologyTagger, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new NormalizationHandler(_rawPostings, null!, _companies, _normalizers, _technologyTagger, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new NormalizationHandler(_rawPostings, _sources, null!, _normalizers, _technologyTagger, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new NormalizationHandler(_rawPostings, _sources, _companies, null!, _technologyTagger, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new NormalizationHandler(_rawPostings, _sources, _companies, _normalizers, null!, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new NormalizationHandler(_rawPostings, _sources, _companies, _normalizers, _technologyTagger, null!, logger));
        Should.Throw<ArgumentNullException>(() => new NormalizationHandler(_rawPostings, _sources, _companies, _normalizers, _technologyTagger, _clock, null!));
    }
}
