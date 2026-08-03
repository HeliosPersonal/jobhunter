using System.Net;

namespace JobHunter.Infrastructure.Http;

/// <summary>
/// Wraps an <see cref="HttpContent"/> so reading its body throws the instant it crosses the configured
/// cap, rather than buffering an unbounded response into memory (security §4, SAD §8). This is what makes
/// the 10 MB limit enforceable even when a provider streams a multi-megabyte board with no
/// <c>Content-Length</c>: the read is abandoned mid-stream, not after the fact.
/// </summary>
internal sealed class CappedContent : HttpContent
{
    private readonly HttpContent _inner;
    private readonly long _maxBytes;

    public CappedContent(HttpContent inner, long maxBytes)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);

        _inner = inner;
        _maxBytes = maxBytes;

        foreach (var header in inner.Headers)
        {
            Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        await using var source = await _inner.ReadAsStreamAsync().ConfigureAwait(false);
        await CopyWithCapAsync(source, stream, CancellationToken.None).ConfigureAwait(false);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    internal async Task CopyWithCapAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > _maxBytes)
            {
                throw new ResponseTooLargeException(_maxBytes);
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Reads the whole body into a stream, enforcing the cap while it streams.</summary>
    public async Task<Stream> ReadCappedAsync(CancellationToken cancellationToken = default)
    {
        await using var source = await _inner.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var destination = new MemoryStream();
        await CopyWithCapAsync(source, destination, cancellationToken).ConfigureAwait(false);
        destination.Position = 0;
        return destination;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>Raised when a response body exceeds the configured cap; an infrastructure fault, not a value.</summary>
internal sealed class ResponseTooLargeException(long maxBytes)
    : Exception($"Response body exceeded the {maxBytes}-byte cap and was abandoned.")
{
    public long MaxBytes { get; } = maxBytes;
}
