using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Application.Normalization;

/// <summary>
/// Stage 2: turns one ingested raw posting into a candidate canonical job (SAD §6.1). It loads the stored
/// payload, resolves the provider normaliser by the source's <see cref="AtsKind"/>, extracts the canonical
/// fields, runs the shared normalisers and the fingerprint through <see cref="CandidateJobFactory"/>, and
/// publishes <see cref="JobNormalized"/> for the deduplication stage. It never writes a job — the
/// deduplication handler is the concurrency arbiter (that is what keeps the two stages idempotent on
/// different keys).
///
/// <para>Idempotent on the raw posting id: the candidate job id is derived deterministically from the raw
/// posting id, so replaying the same <see cref="RawPostingIngested"/> re-publishes the same
/// <see cref="JobNormalized"/> with the same candidate id and fingerprint — the downstream insert then
/// conflicts and records an alias rather than a second job. A payload missing a required field (title,
/// apply URL), a malformed payload, an unroutable provider or a missing company is recorded as a
/// normalisation failure and the message ends cleanly; one bad posting never halts the batch (AC-04).</para>
/// </summary>
public sealed class NormalizationHandler(
    IRawPostingReader rawPostings,
    IJobSourceRepository sources,
    ICompanyRepository companies,
    IPostingNormalizerCatalog normalizers,
    TechnologyTagger technologyTagger,
    IClock clock,
    ILogger<NormalizationHandler> logger)
{
    private readonly IRawPostingReader _rawPostings = rawPostings ?? throw new ArgumentNullException(nameof(rawPostings));
    private readonly IJobSourceRepository _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    private readonly ICompanyRepository _companies = companies ?? throw new ArgumentNullException(nameof(companies));
    private readonly IPostingNormalizerCatalog _normalizers = normalizers ?? throw new ArgumentNullException(nameof(normalizers));
    private readonly TechnologyTagger _technologyTagger = technologyTagger ?? throw new ArgumentNullException(nameof(technologyTagger));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<NormalizationHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task Handle(
        RawPostingIngested message,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);

        var raw = await _rawPostings.FindAsync(message.RawPostingId, cancellationToken).ConfigureAwait(false);
        if (raw is null)
        {
            _logger.LogWarning(
                "Raw posting {RawPostingId} no longer exists; skipping normalisation.", message.RawPostingId);
            return;
        }

        var source = await _sources.FindAsync(message.SourceId, cancellationToken).ConfigureAwait(false);
        var binding = source is null
            ? null
            : await _companies.FindBindingAsync(source.BindingId, cancellationToken).ConfigureAwait(false);
        if (binding is null)
        {
            _logger.LogWarning(
                "Raw posting {RawPostingId} has no resolvable binding; recording normalisation failure.",
                message.RawPostingId);
            return;
        }

        var normalizer = _normalizers.For(binding.AtsKind);
        if (normalizer is null)
        {
            _logger.LogWarning(
                "No normaliser registered for ATS kind {AtsKind}; raw posting {RawPostingId} is unroutable.",
                binding.AtsKind, message.RawPostingId);
            return;
        }

        var company = await _companies.FindAsync(message.CompanyId, cancellationToken).ConfigureAwait(false);
        if (company is null)
        {
            _logger.LogWarning(
                "Company {CompanyId} for raw posting {RawPostingId} not found; recording normalisation failure.",
                message.CompanyId, message.RawPostingId);
            return;
        }

        var extraction = normalizer.Extract(raw.Payload);
        if (extraction.IsFailure)
        {
            _logger.LogWarning(
                "Normalisation of raw posting {RawPostingId} ({AtsKind}) failed: {Reason}.",
                message.RawPostingId, binding.AtsKind, extraction.Error.Code);
            return;
        }

        var jobId = CandidateJobId.For(message.RawPostingId);
        var context = new NormalizationContext(
            message.CompanyId,
            message.RawPostingId,
            message.SourceId,
            company.CanonicalDomain.Value,
            raw.FetchedAt,
            raw.LastSeenAt);

        var candidate = CandidateJobFactory.Create(jobId, extraction.Value, context, _technologyTagger);
        if (candidate.IsFailure)
        {
            _logger.LogWarning(
                "Raw posting {RawPostingId} ({AtsKind}) is missing a required field: {Reason}.",
                message.RawPostingId, binding.AtsKind, candidate.Error.Code);
            return;
        }

        var job = candidate.Value;
        await bus.PublishAsync(new JobNormalized(
            job.Id,
            message.RawPostingId,
            message.CompanyId,
            job.Fingerprint.Value,
            _clock.UtcNow)).ConfigureAwait(false);

        _logger.LogInformation(
            "Normalised raw posting {RawPostingId} ({AtsKind}) into candidate job {JobId}.",
            message.RawPostingId, binding.AtsKind, job.Id);
    }
}
