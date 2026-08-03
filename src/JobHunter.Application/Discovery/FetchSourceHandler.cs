using JobHunter.Application.Common;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Postings;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Application.Discovery;

/// <summary>
/// Fetches one board (SAD §6.1). The cycle fanned out one <see cref="SourceFetchRequested"/> per source
/// and RabbitMQ delivers them with the bounded degree (SAD §8), so this handler is deliberately single-
/// source: it resolves the source's live binding and its provider adapter, fetches the board, and streams
/// every posting through the ingestion upsert. One board is one failure domain (QG-1) — a provider that
/// 500s or a source deleted mid-flight ends this message cleanly and never touches another source.
///
/// Ingestion is the T11 insert path: each posting goes through the single-statement dedup-and-refresh
/// upsert (<see cref="IRawPostingRepository"/>), which distinguishes a genuine insert from an unchanged
/// re-fetch via the <c>xmax = 0</c> trick. <see cref="RawPostingIngested"/> is published exactly once per
/// distinct content — an unchanged re-fetch only bumps <c>last_seen_at</c> and emits nothing (AC-02). The
/// fraction of a board unchanged since the last fetch is exported as
/// <see cref="Telemetry.RawPostingsUnchangedRatio"/> (expected ≈ 0.90).
/// </summary>
public sealed class FetchSourceHandler(
    IJobSourceRepository sources,
    ICompanyRepository companies,
    IJobSourceCatalog catalog,
    IRawPostingRepository rawPostings,
    IIdGenerator ids,
    IClock clock,
    ILogger<FetchSourceHandler> logger)
{
    private readonly IJobSourceRepository _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    private readonly ICompanyRepository _companies = companies ?? throw new ArgumentNullException(nameof(companies));
    private readonly IJobSourceCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly IRawPostingRepository _rawPostings = rawPostings ?? throw new ArgumentNullException(nameof(rawPostings));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<FetchSourceHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task Handle(SourceFetchRequested message, IMessageBus bus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);

        var source = await _sources.FindAsync(message.SourceId, cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            // The source was retired/deleted while this message sat in the queue. Nothing to fetch — exit
            // cleanly rather than fault the message (AC: "deleted while in flight exits cleanly").
            _logger.LogInformation(
                "Source {SourceId} no longer exists; skipping in-flight fetch request.", message.SourceId);
            return;
        }

        var binding = await _companies.FindBindingAsync(source.BindingId, cancellationToken).ConfigureAwait(false);
        if (binding is null || !binding.IsLive)
        {
            _logger.LogInformation(
                "Source {SourceId} has no live binding; skipping fetch.", message.SourceId);
            return;
        }

        var adapter = _catalog.For(binding.AtsKind);
        if (adapter is null)
        {
            // No adapter is registered for this provider — unroutable, not a fault. Log and stop.
            _logger.LogWarning(
                "No adapter registered for ATS kind {AtsKind}; source {SourceId} is unroutable.",
                binding.AtsKind, message.SourceId);
            return;
        }

        await FetchBoardAsync(adapter, binding, message, bus, cancellationToken).ConfigureAwait(false);
    }

    private async Task FetchBoardAsync(
        IJobSource adapter,
        AtsBinding binding,
        SourceFetchRequested message,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var fetch = await adapter.FetchBoardAsync(binding, cancellationToken).ConfigureAwait(false);

        var returned = 0;
        var changed = 0;
        await foreach (var posting in fetch.Postings.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            returned++;
            if (await IngestAsync(posting, message, fetch.HttpStatus, bus, cancellationToken).ConfigureAwait(false))
            {
                changed++;
            }
        }

        // The share of the board unchanged since the last fetch (AC-02). Only meaningful when the board
        // returned something — an empty board records no ratio rather than a divide-by-zero.
        if (returned > 0)
        {
            var unchangedRatio = (double)(returned - changed) / returned;
            Telemetry.RawPostingsUnchangedRatio.Record(
                unchangedRatio,
                new KeyValuePair<string, object?>(TelemetryLabels.AtsKind, binding.AtsKind.ToString()));
        }

        _logger.LogInformation(
            "Fetched board for binding {BindingId} ({AtsKind}): outcome {Outcome}, {Returned} posting(s), {Changed} changed.",
            binding.Id, binding.AtsKind, fetch.Outcome, returned, changed);
    }

    /// <summary>
    /// Ingests one posting through the dedup-and-refresh upsert and publishes <see cref="RawPostingIngested"/>
    /// only on a genuine insert. Returns <c>true</c> when the content was new (a row was inserted), <c>false</c>
    /// when it was an unchanged re-fetch that merely bumped <c>last_seen_at</c>. A posting whose content hash is
    /// malformed is skipped (a bad board row is not the board's failure — QG-1), counting as no change.
    /// </summary>
    private async Task<bool> IngestAsync(
        FetchedPosting posting,
        SourceFetchRequested message,
        short httpStatus,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var hashResult = ContentHash.TryCreate(posting.ContentHash);
        if (hashResult.IsFailure)
        {
            _logger.LogWarning(
                "Posting {ExternalId} on source {SourceId} carried a malformed content hash; skipping.",
                posting.ExternalId, message.SourceId);
            return false;
        }

        var raw = new RawPosting(
            _ids.NewId(),
            message.SourceId,
            posting.ExternalId,
            hashResult.Value,
            posting.RawPayload,
            httpStatus,
            _clock.UtcNow);

        var outcome = await _rawPostings.IngestAsync(raw, cancellationToken).ConfigureAwait(false);
        if (outcome != IngestOutcome.Inserted)
        {
            return false;
        }

        await bus.PublishAsync(new RawPostingIngested(
            raw.Id, message.SourceId, message.CompanyId, hashResult.Value.Value, _clock.UtcNow))
            .ConfigureAwait(false);

        return true;
    }
}
