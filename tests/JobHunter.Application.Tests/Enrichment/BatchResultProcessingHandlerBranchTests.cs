using JobHunter.Application.Enrichment;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Enrichment;

/// <summary>
/// The dependency-guard arm of <see cref="BatchResultProcessingHandler"/>: every injected port is
/// null-checked in the field initialiser, so a missing collaborator fails fast at construction.
/// </summary>
public sealed class BatchResultProcessingHandlerBranchTests
{
    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly IEnrichmentRepository _enrichments = Substitute.For<IEnrichmentRepository>();
    private readonly IEnrichmentResultParser _parser = Substitute.For<IEnrichmentResultParser>();
    private readonly ILlmBatchClient _client = Substitute.For<ILlmBatchClient>();
    private readonly ICostAccountant _accountant = Substitute.For<ICostAccountant>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var logger = NullLogger<BatchResultProcessingHandler>.Instance;

        Should.Throw<ArgumentNullException>(() => new BatchResultProcessingHandler(null!, _enrichments, _parser, _client, _accountant, _clock, _ids, logger));
        Should.Throw<ArgumentNullException>(() => new BatchResultProcessingHandler(_runs, null!, _parser, _client, _accountant, _clock, _ids, logger));
        Should.Throw<ArgumentNullException>(() => new BatchResultProcessingHandler(_runs, _enrichments, null!, _client, _accountant, _clock, _ids, logger));
        Should.Throw<ArgumentNullException>(() => new BatchResultProcessingHandler(_runs, _enrichments, _parser, null!, _accountant, _clock, _ids, logger));
        Should.Throw<ArgumentNullException>(() => new BatchResultProcessingHandler(_runs, _enrichments, _parser, _client, null!, _clock, _ids, logger));
        Should.Throw<ArgumentNullException>(() => new BatchResultProcessingHandler(_runs, _enrichments, _parser, _client, _accountant, null!, _ids, logger));
        Should.Throw<ArgumentNullException>(() => new BatchResultProcessingHandler(_runs, _enrichments, _parser, _client, _accountant, _clock, null!, logger));
        Should.Throw<ArgumentNullException>(() => new BatchResultProcessingHandler(_runs, _enrichments, _parser, _client, _accountant, _clock, _ids, null!));
    }
}
