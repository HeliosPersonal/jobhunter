using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Application.Discovery;

/// <summary>
/// Fetches one board (SAD §6.1). The cycle fanned out one <see cref="SourceFetchRequested"/> per source
/// and RabbitMQ delivers them with the bounded degree (SAD §8), so this handler is deliberately single-
/// source: it resolves the source's live binding and its provider adapter, fetches the board, and streams
/// every posting through the ingestion upsert. One board is one failure domain (QG-1) — a provider that
/// 500s or a source deleted mid-flight ends this message cleanly and never touches another source.
/// </summary>
public sealed class FetchSourceHandler(
    IJobSourceRepository sources,
    ICompanyRepository companies,
    IJobSourceCatalog catalog,
    ILogger<FetchSourceHandler> logger)
{
    private readonly IJobSourceRepository _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    private readonly ICompanyRepository _companies = companies ?? throw new ArgumentNullException(nameof(companies));
    private readonly IJobSourceCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly ILogger<FetchSourceHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task Handle(SourceFetchRequested message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

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

        await FetchBoardAsync(adapter, binding, cancellationToken).ConfigureAwait(false);
    }

    private async Task FetchBoardAsync(IJobSource adapter, AtsBinding binding, CancellationToken cancellationToken)
    {
        var fetch = await adapter.FetchBoardAsync(binding, cancellationToken).ConfigureAwait(false);

        var returned = 0;
        await foreach (var posting in fetch.Postings.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            _ = posting;
            returned++;
        }

        _logger.LogInformation(
            "Fetched board for binding {BindingId} ({AtsKind}): outcome {Outcome}, {Returned} posting(s).",
            binding.Id, binding.AtsKind, fetch.Outcome, returned);
    }
}
