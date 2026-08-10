using JobHunter.Application.Preferences;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Preferences;

/// <summary>
/// The dependency-guard arm of <see cref="PreferenceModelQuery"/>: every injected collaborator is null-checked
/// in the field initialiser, so a missing dependency fails fast at construction rather than at first query.
/// </summary>
public sealed class PreferenceModelQueryBranchTests
{
    private readonly IPreferenceModelRepository _models = Substitute.For<IPreferenceModelRepository>();
    private readonly IProfileRepository _profiles = Substitute.For<IProfileRepository>();
    private readonly IJobFactsSnapshotQuery _facts = Substitute.For<IJobFactsSnapshotQuery>();
    private readonly ILearningSwitch _learning = Substitute.For<ILearningSwitch>();

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var logger = NullLogger<PreferenceModelQuery>.Instance;

        Should.Throw<ArgumentNullException>(() => new PreferenceModelQuery(null!, _profiles, _facts, _learning, logger));
        Should.Throw<ArgumentNullException>(() => new PreferenceModelQuery(_models, null!, _facts, _learning, logger));
        Should.Throw<ArgumentNullException>(() => new PreferenceModelQuery(_models, _profiles, null!, _learning, logger));
        Should.Throw<ArgumentNullException>(() => new PreferenceModelQuery(_models, _profiles, _facts, null!, logger));
        Should.Throw<ArgumentNullException>(() => new PreferenceModelQuery(_models, _profiles, _facts, _learning, null!));
    }
}
