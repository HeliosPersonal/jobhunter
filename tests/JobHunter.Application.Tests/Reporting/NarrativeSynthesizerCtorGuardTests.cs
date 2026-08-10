using JobHunter.Application.Reporting;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Reporting;

/// <summary>
/// The dependency-guard arm of <see cref="NarrativeSynthesizer"/>: every injected collaborator and the
/// synthesis options are null-checked in the field initialiser, so a missing dependency fails fast at
/// construction rather than at first synthesis.
/// </summary>
public sealed class NarrativeSynthesizerCtorGuardTests
{
    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly INarrativeRequestBuilder _requestBuilder = Substitute.For<INarrativeRequestBuilder>();
    private readonly INarrativeResultParser _resultParser = Substitute.For<INarrativeResultParser>();
    private readonly ICostAccountant _accountant = Substitute.For<ICostAccountant>();
    private readonly ILlmBatchClient _client = Substitute.For<ILlmBatchClient>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly NarrativeSynthesisOptions _options = new();

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var logger = NullLogger<NarrativeSynthesizer>.Instance;

        Should.Throw<ArgumentNullException>(() => new NarrativeSynthesizer(null!, _requestBuilder, _resultParser, _accountant, _client, _clock, _ids, _options, logger));
        Should.Throw<ArgumentNullException>(() => new NarrativeSynthesizer(_runs, null!, _resultParser, _accountant, _client, _clock, _ids, _options, logger));
        Should.Throw<ArgumentNullException>(() => new NarrativeSynthesizer(_runs, _requestBuilder, null!, _accountant, _client, _clock, _ids, _options, logger));
        Should.Throw<ArgumentNullException>(() => new NarrativeSynthesizer(_runs, _requestBuilder, _resultParser, null!, _client, _clock, _ids, _options, logger));
        Should.Throw<ArgumentNullException>(() => new NarrativeSynthesizer(_runs, _requestBuilder, _resultParser, _accountant, null!, _clock, _ids, _options, logger));
        Should.Throw<ArgumentNullException>(() => new NarrativeSynthesizer(_runs, _requestBuilder, _resultParser, _accountant, _client, null!, _ids, _options, logger));
        Should.Throw<ArgumentNullException>(() => new NarrativeSynthesizer(_runs, _requestBuilder, _resultParser, _accountant, _client, _clock, null!, _options, logger));
        Should.Throw<ArgumentNullException>(() => new NarrativeSynthesizer(_runs, _requestBuilder, _resultParser, _accountant, _client, _clock, _ids, null!, logger));
        Should.Throw<ArgumentNullException>(() => new NarrativeSynthesizer(_runs, _requestBuilder, _resultParser, _accountant, _client, _clock, _ids, _options, null!));
    }
}
