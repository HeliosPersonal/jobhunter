using JobHunter.Application.Enrichment;
using JobHunter.Application.Matching;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Matching;

/// <summary>
/// The dependency-guard arm of <see cref="MatchingPollHandler"/>: every injected port is null-checked in the
/// field initialiser, so a missing collaborator fails fast at construction.
/// </summary>
public sealed class MatchingPollHandlerBranchTests
{
    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly ILlmBatchClient _client = Substitute.For<ILlmBatchClient>();
    private readonly IJitter _jitter = Substitute.For<IJitter>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly PollOptions _options = new();

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var logger = NullLogger<MatchingPollHandler>.Instance;

        Should.Throw<ArgumentNullException>(() => new MatchingPollHandler(null!, _client, _jitter, _clock, _options, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingPollHandler(_runs, null!, _jitter, _clock, _options, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingPollHandler(_runs, _client, null!, _clock, _options, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingPollHandler(_runs, _client, _jitter, null!, _options, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingPollHandler(_runs, _client, _jitter, _clock, null!, logger));
        Should.Throw<ArgumentNullException>(() => new MatchingPollHandler(_runs, _client, _jitter, _clock, _options, null!));
    }
}
