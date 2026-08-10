using JobHunter.Application.Enrichment;
using JobHunter.Application.Matching;
using JobHunter.Domain.Abstractions;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Matching;

/// <summary>
/// The dependency-guard arm of <see cref="MatchingSubmitHandler"/>: every injected port is null-checked in the
/// field initialiser, so a missing collaborator fails fast at construction rather than with a
/// <see cref="NullReferenceException"/> deep in the Run. One assertion per constructor position.
/// </summary>
public sealed class MatchingSubmitHandlerBranchTests
{
    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly IMatchScopeQuery _scope = Substitute.For<IMatchScopeQuery>();
    private readonly IReMatchBacklog _reMatchBacklog = Substitute.For<IReMatchBacklog>();
    private readonly IMatchRequestBuilder _builder = Substitute.For<IMatchRequestBuilder>();
    private readonly IProfileRepository _profiles = Substitute.For<IProfileRepository>();
    private readonly ICvVersionRepository _cvVersions = Substitute.For<ICvVersionRepository>();
    private readonly ICurrentMatchQuery _currentMatches = Substitute.For<ICurrentMatchQuery>();
    private readonly IScoreRepository _scores = Substitute.For<IScoreRepository>();
    private readonly ICostAccountant _accountant = Substitute.For<ICostAccountant>();
    private readonly ILlmBatchClient _client = Substitute.For<ILlmBatchClient>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly RunOptions _runOptions = new();
    private readonly PreMatchOptions _preMatchOptions = new();

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var logger = NullLogger<MatchingSubmitHandler>.Instance;

        Should.Throw<ArgumentNullException>(() => new MatchingSubmitHandler(null!, _scope, _reMatchBacklog, _builder, _profiles, _cvVersions, _currentMatches, _scores, _accountant, _client, _clock, _ids, _runOptions, _preMatchOptions, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingSubmitHandler(_runs, null!, _reMatchBacklog, _builder, _profiles, _cvVersions, _currentMatches, _scores, _accountant, _client, _clock, _ids, _runOptions, _preMatchOptions, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingSubmitHandler(_runs, _scope, null!, _builder, _profiles, _cvVersions, _currentMatches, _scores, _accountant, _client, _clock, _ids, _runOptions, _preMatchOptions, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingSubmitHandler(_runs, _scope, _reMatchBacklog, null!, _profiles, _cvVersions, _currentMatches, _scores, _accountant, _client, _clock, _ids, _runOptions, _preMatchOptions, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingSubmitHandler(_runs, _scope, _reMatchBacklog, _builder, null!, _cvVersions, _currentMatches, _scores, _accountant, _client, _clock, _ids, _runOptions, _preMatchOptions, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingSubmitHandler(_runs, _scope, _reMatchBacklog, _builder, _profiles, null!, _currentMatches, _scores, _accountant, _client, _clock, _ids, _runOptions, _preMatchOptions, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingSubmitHandler(_runs, _scope, _reMatchBacklog, _builder, _profiles, _cvVersions, null!, _scores, _accountant, _client, _clock, _ids, _runOptions, _preMatchOptions, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingSubmitHandler(_runs, _scope, _reMatchBacklog, _builder, _profiles, _cvVersions, _currentMatches, null!, _accountant, _client, _clock, _ids, _runOptions, _preMatchOptions, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingSubmitHandler(_runs, _scope, _reMatchBacklog, _builder, _profiles, _cvVersions, _currentMatches, _scores, null!, _client, _clock, _ids, _runOptions, _preMatchOptions, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingSubmitHandler(_runs, _scope, _reMatchBacklog, _builder, _profiles, _cvVersions, _currentMatches, _scores, _accountant, null!, _clock, _ids, _runOptions, _preMatchOptions, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingSubmitHandler(_runs, _scope, _reMatchBacklog, _builder, _profiles, _cvVersions, _currentMatches, _scores, _accountant, _client, null!, _ids, _runOptions, _preMatchOptions, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingSubmitHandler(_runs, _scope, _reMatchBacklog, _builder, _profiles, _cvVersions, _currentMatches, _scores, _accountant, _client, _clock, null!, _runOptions, _preMatchOptions, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingSubmitHandler(_runs, _scope, _reMatchBacklog, _builder, _profiles, _cvVersions, _currentMatches, _scores, _accountant, _client, _clock, _ids, null!, _preMatchOptions, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingSubmitHandler(_runs, _scope, _reMatchBacklog, _builder, _profiles, _cvVersions, _currentMatches, _scores, _accountant, _client, _clock, _ids, _runOptions, null!, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingSubmitHandler(_runs, _scope, _reMatchBacklog, _builder, _profiles, _cvVersions, _currentMatches, _scores, _accountant, _client, _clock, _ids, _runOptions, _preMatchOptions, null!));
    }
}
