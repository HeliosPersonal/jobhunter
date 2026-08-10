using JobHunter.Application.Ratings;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Ratings;

/// <summary>
/// The dependency-guard arm of <see cref="RegretMatcher"/>: every injected collaborator and the matching
/// options are null-checked in the field initialiser, so a missing dependency fails fast at construction
/// rather than at first match.
/// </summary>
public sealed class RegretMatcherCtorGuardTests
{
    private readonly IProfileRepository _profiles = Substitute.For<IProfileRepository>();
    private readonly ICvVersionRepository _cvVersions = Substitute.For<ICvVersionRepository>();
    private readonly IMatchRequestBuilder _requestBuilder = Substitute.For<IMatchRequestBuilder>();
    private readonly IMatchResultParser _resultParser = Substitute.For<IMatchResultParser>();
    private readonly ILlmBatchClient _client = Substitute.For<ILlmBatchClient>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly RegretMatchingOptions _options = new();

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var logger = NullLogger<RegretMatcher>.Instance;

        Should.Throw<ArgumentNullException>(() => new RegretMatcher(null!, _cvVersions, _requestBuilder, _resultParser, _client, _clock, _ids, _options, logger));
        Should.Throw<ArgumentNullException>(() => new RegretMatcher(_profiles, null!, _requestBuilder, _resultParser, _client, _clock, _ids, _options, logger));
        Should.Throw<ArgumentNullException>(() => new RegretMatcher(_profiles, _cvVersions, null!, _resultParser, _client, _clock, _ids, _options, logger));
        Should.Throw<ArgumentNullException>(() => new RegretMatcher(_profiles, _cvVersions, _requestBuilder, null!, _client, _clock, _ids, _options, logger));
        Should.Throw<ArgumentNullException>(() => new RegretMatcher(_profiles, _cvVersions, _requestBuilder, _resultParser, null!, _clock, _ids, _options, logger));
        Should.Throw<ArgumentNullException>(() => new RegretMatcher(_profiles, _cvVersions, _requestBuilder, _resultParser, _client, null!, _ids, _options, logger));
        Should.Throw<ArgumentNullException>(() => new RegretMatcher(_profiles, _cvVersions, _requestBuilder, _resultParser, _client, _clock, null!, _options, logger));
        Should.Throw<ArgumentNullException>(() => new RegretMatcher(_profiles, _cvVersions, _requestBuilder, _resultParser, _client, _clock, _ids, null!, logger));
        Should.Throw<ArgumentNullException>(() => new RegretMatcher(_profiles, _cvVersions, _requestBuilder, _resultParser, _client, _clock, _ids, _options, null!));
    }
}
