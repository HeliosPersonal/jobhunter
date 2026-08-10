using JobHunter.Application.Matching;
using JobHunter.Domain.Abstractions;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Matching;

/// <summary>
/// The dependency-guard arm of <see cref="MatchingResultProcessingHandler"/>: every injected port is
/// null-checked in the field initialiser, so a missing collaborator fails fast at construction.
/// </summary>
public sealed class MatchingResultProcessingHandlerBranchTests
{
    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly IMatchRepository _matches = Substitute.For<IMatchRepository>();
    private readonly IMatchResultParser _parser = Substitute.For<IMatchResultParser>();
    private readonly IProfileRepository _profiles = Substitute.For<IProfileRepository>();
    private readonly ICvVersionRepository _cvVersions = Substitute.For<ICvVersionRepository>();
    private readonly ILlmBatchClient _client = Substitute.For<ILlmBatchClient>();
    private readonly ICostAccountant _accountant = Substitute.For<ICostAccountant>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var logger = NullLogger<MatchingResultProcessingHandler>.Instance;

        Should.Throw<ArgumentNullException>(() => new MatchingResultProcessingHandler(null!, _matches, _parser, _profiles, _cvVersions, _client, _accountant, _clock, _ids, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingResultProcessingHandler(_runs, null!, _parser, _profiles, _cvVersions, _client, _accountant, _clock, _ids, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingResultProcessingHandler(_runs, _matches, null!, _profiles, _cvVersions, _client, _accountant, _clock, _ids, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingResultProcessingHandler(_runs, _matches, _parser, null!, _cvVersions, _client, _accountant, _clock, _ids, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingResultProcessingHandler(_runs, _matches, _parser, _profiles, null!, _client, _accountant, _clock, _ids, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingResultProcessingHandler(_runs, _matches, _parser, _profiles, _cvVersions, null!, _accountant, _clock, _ids, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingResultProcessingHandler(_runs, _matches, _parser, _profiles, _cvVersions, _client, null!, _clock, _ids, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingResultProcessingHandler(_runs, _matches, _parser, _profiles, _cvVersions, _client, _accountant, null!, _ids, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingResultProcessingHandler(_runs, _matches, _parser, _profiles, _cvVersions, _client, _accountant, _clock, null!, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingResultProcessingHandler(_runs, _matches, _parser, _profiles, _cvVersions, _client, _accountant, _clock, _ids, null!));
    }
}
