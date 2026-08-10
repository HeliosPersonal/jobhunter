using JobHunter.Application.Enrichment;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Enrichment;

/// <summary>
/// The dependency-guard arm of <see cref="EnrichmentSubmitHandler"/>: every injected port is null-checked in
/// the field initialiser, so a missing collaborator fails fast at construction.
/// </summary>
public sealed class EnrichmentSubmitHandlerBranchTests
{
    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly IEnrichmentScopeQuery _scope = Substitute.For<IEnrichmentScopeQuery>();
    private readonly IEnrichmentRequestBuilder _builder = Substitute.For<IEnrichmentRequestBuilder>();
    private readonly ICostAccountant _accountant = Substitute.For<ICostAccountant>();
    private readonly ILlmBatchClient _client = Substitute.For<ILlmBatchClient>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var logger = NullLogger<EnrichmentSubmitHandler>.Instance;

        Should.Throw<ArgumentNullException>(() => new EnrichmentSubmitHandler(null!, _scope, _builder, _accountant, _client, _clock, _ids, logger));
        Should.Throw<ArgumentNullException>(() => new EnrichmentSubmitHandler(_runs, null!, _builder, _accountant, _client, _clock, _ids, logger));
        Should.Throw<ArgumentNullException>(() => new EnrichmentSubmitHandler(_runs, _scope, null!, _accountant, _client, _clock, _ids, logger));
        Should.Throw<ArgumentNullException>(() => new EnrichmentSubmitHandler(_runs, _scope, _builder, null!, _client, _clock, _ids, logger));
        Should.Throw<ArgumentNullException>(() => new EnrichmentSubmitHandler(_runs, _scope, _builder, _accountant, null!, _clock, _ids, logger));
        Should.Throw<ArgumentNullException>(() => new EnrichmentSubmitHandler(_runs, _scope, _builder, _accountant, _client, null!, _ids, logger));
        Should.Throw<ArgumentNullException>(() => new EnrichmentSubmitHandler(_runs, _scope, _builder, _accountant, _client, _clock, null!, logger));
        Should.Throw<ArgumentNullException>(() => new EnrichmentSubmitHandler(_runs, _scope, _builder, _accountant, _client, _clock, _ids, null!));
    }
}
