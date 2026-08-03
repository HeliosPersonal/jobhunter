using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Common;
using JobHunter.Domain.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunter.Search;

/// <summary>
/// The Typesense adapter behind <see cref="ISearchIndex"/> (SAD §5, F9-T02). It is the only type that
/// knows Typesense's HTTP shape — the indexing handler and the rebuild depend on the port, never on this.
/// Every method returns a <see cref="Result{T}"/>: an unreachable index, a timeout or a non-success
/// status is an expected business outcome the caller retries or dead-letters, never an exception thrown
/// into the pipeline (QG-3, coding-standards §1). The collection name is <c>{env}_jobhunter_jobs</c> and
/// the document id is the job id, so an upsert is idempotent with no lookup (SAD §8).
/// </summary>
public sealed class TypesenseIndexer : ISearchIndex
{
    /// <summary>The named <see cref="HttpClient"/> the DI registration configures with base URL and key.</summary>
    public const string HttpClientName = "Typesense";

    private static readonly Error Unavailable =
        new("search.index.unavailable", "The search index is unavailable.");

    private readonly HttpClient _http;
    private readonly TypesenseOptions _options;
    private readonly ILogger<TypesenseIndexer> _logger;

    public TypesenseIndexer(
        HttpClient http,
        IOptions<TypesenseOptions> options,
        ILogger<TypesenseIndexer> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<bool>> EnsureCollectionAsync(CancellationToken cancellationToken = default)
    {
        // Idempotent: a present collection is a 409 on create, which is success, not a fault (done-when).
        var body = TypesenseSerialization.SchemaJson(_options.CollectionName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "collections")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        return await SendAsync(
            request,
            onSuccess: _ => true,
            allowConflictAsSuccess: true,
            allowNotFoundAsSuccess: false,
            operation: "ensure-collection",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<bool>> UpsertAsync(JobDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var body = TypesenseSerialization.DocumentJson(document);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"collections/{_options.CollectionName}/documents?action=upsert")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        return await SendAsync(
            request,
            onSuccess: _ => true,
            allowConflictAsSuccess: false,
            allowNotFoundAsSuccess: false,
            operation: "upsert",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<int>> UpsertManyAsync(
        IReadOnlyList<JobDocument> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0)
        {
            return Result<int>.Success(0);
        }

        var body = TypesenseSerialization.DocumentsJsonl(documents);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"collections/{_options.CollectionName}/documents/import?action=upsert")
        {
            Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue("text/plain")),
        };

        // The import endpoint answers 200 with one JSON-per-line status; a per-document failure is reported
        // there, not as a transport error. We count the lines reporting success.
        return await SendAsync(
            request,
            onSuccess: responseBody => CountImportSuccesses(responseBody, documents.Count),
            allowConflictAsSuccess: false,
            allowNotFoundAsSuccess: false,
            operation: "upsert-many",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<bool>> DeleteAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"collections/{_options.CollectionName}/documents/{jobId}");

        // Deleting an absent document answers 404 — a success here (done-when: "deleting an absent document
        // is a success"), so the delete is idempotent under redelivery.
        return await SendAsync(
            request,
            onSuccess: _ => true,
            allowConflictAsSuccess: false,
            allowNotFoundAsSuccess: true,
            operation: "delete",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<long>> CountAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"collections/{_options.CollectionName}");

        return await SendAsync(
            request,
            onSuccess: ReadDocumentCount,
            allowConflictAsSuccess: false,
            allowNotFoundAsSuccess: false,
            operation: "count",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<bool>> DropAndRecreateAsync(CancellationToken cancellationToken = default)
    {
        using var drop = new HttpRequestMessage(
            HttpMethod.Delete,
            $"collections/{_options.CollectionName}");

        var dropped = await SendAsync(
            drop,
            onSuccess: _ => true,
            allowConflictAsSuccess: false,
            allowNotFoundAsSuccess: true,
            operation: "drop",
            cancellationToken).ConfigureAwait(false);

        if (dropped.IsFailure)
        {
            return dropped;
        }

        return await EnsureCollectionAsync(cancellationToken).ConfigureAwait(false);
    }

    private static int CountImportSuccesses(string responseBody, int total)
    {
        if (string.IsNullOrEmpty(responseBody))
        {
            return total;
        }

        var successes = 0;
        foreach (var line in responseBody.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("success", out var success) && success.GetBoolean())
                {
                    successes++;
                }
            }
            catch (JsonException)
            {
                // A line we cannot parse is not a counted success; the caller sees fewer than requested.
            }
        }

        return successes;
    }

    private static long ReadDocumentCount(string responseBody)
    {
        if (string.IsNullOrEmpty(responseBody))
        {
            return 0;
        }

        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.TryGetProperty("num_documents", out var count)
            ? count.GetInt64()
            : 0;
    }

    private async Task<Result<T>> SendAsync<T>(
        HttpRequestMessage request,
        Func<string, T> onSuccess,
        bool allowConflictAsSuccess,
        bool allowNotFoundAsSuccess,
        string operation,
        CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation("X-TYPESENSE-API-KEY", _options.ApiKey);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode
                || (allowConflictAsSuccess && response.StatusCode == HttpStatusCode.Conflict)
                || (allowNotFoundAsSuccess && response.StatusCode == HttpStatusCode.NotFound))
            {
                var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return Result<T>.Success(onSuccess(payload));
            }

            // A non-success status is an unavailable/degraded index: a value, retried by the caller. The
            // status is logged for the operator; the api key never appears (invariant 12).
            _logger.LogWarning(
                "Typesense {Operation} returned {StatusCode}; treating the index as unavailable.",
                operation, (int)response.StatusCode);
            return Result<T>.Failure(Unavailable);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Typesense {Operation} could not reach the index.", operation);
            return Result<T>.Failure(Unavailable);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A timeout, not a caller cancellation — an unavailable index, reported as a value (QG-3).
            _logger.LogWarning(ex, "Typesense {Operation} timed out.", operation);
            return Result<T>.Failure(Unavailable);
        }
    }
}
