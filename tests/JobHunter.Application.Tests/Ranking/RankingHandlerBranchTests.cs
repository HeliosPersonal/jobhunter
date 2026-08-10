using JobHunter.Application.Ranking;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Ranking;

/// <summary>
/// The dependency-guard arm of <see cref="RankingHandler"/>: every injected port is null-checked in the
/// field initialiser, so a missing collaborator fails fast at construction.
/// </summary>
public sealed class RankingHandlerBranchTests
{
    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly IRankingScopeQuery _scope = Substitute.For<IRankingScopeQuery>();
    private readonly IProfileRepository _profiles = Substitute.For<IProfileRepository>();
    private readonly IPreferenceModelQuery _preferences = Substitute.For<IPreferenceModelQuery>();
    private readonly ISuppressionOverrideQuery _overrides = Substitute.For<ISuppressionOverrideQuery>();
    private readonly IJobFactsSnapshotQuery _facts = Substitute.For<IJobFactsSnapshotQuery>();
    private readonly IScoreRepository _scores = Substitute.For<IScoreRepository>();
    private readonly RankingOptions _options = new();
    private readonly IClock _clock = Substitute.For<IClock>();

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var logger = NullLogger<RankingHandler>.Instance;

        Should.Throw<ArgumentNullException>(() => new RankingHandler(null!, _scope, _profiles, _preferences, _overrides, _facts, _scores, _options, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new RankingHandler(_runs, null!, _profiles, _preferences, _overrides, _facts, _scores, _options, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new RankingHandler(_runs, _scope, null!, _preferences, _overrides, _facts, _scores, _options, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new RankingHandler(_runs, _scope, _profiles, null!, _overrides, _facts, _scores, _options, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new RankingHandler(_runs, _scope, _profiles, _preferences, null!, _facts, _scores, _options, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new RankingHandler(_runs, _scope, _profiles, _preferences, _overrides, null!, _scores, _options, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new RankingHandler(_runs, _scope, _profiles, _preferences, _overrides, _facts, null!, _options, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new RankingHandler(_runs, _scope, _profiles, _preferences, _overrides, _facts, _scores, null!, _clock, logger));
        Should.Throw<ArgumentNullException>(() => new RankingHandler(_runs, _scope, _profiles, _preferences, _overrides, _facts, _scores, _options, null!, logger));
        Should.Throw<ArgumentNullException>(() => new RankingHandler(_runs, _scope, _profiles, _preferences, _overrides, _facts, _scores, _options, _clock, null!));
    }
}
