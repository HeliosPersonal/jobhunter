using System.Net;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Http;

/// <summary>
/// The production <see cref="RobotsPolicy.FetchRobots"/>: it GETs <c>{origin}/robots.txt</c> over a plain
/// <see cref="HttpClient"/> and maps the outcome to a <see cref="RobotsPolicy.RobotsFetch"/> (AC-06). A
/// missing or unreachable file reads permissively (<see cref="RobotsPolicy.RobotsFetch.NotReachable"/> →
/// allow); a body over the sanity cap reads conservatively (<see cref="RobotsPolicy.RobotsFetch.WasMalformed"/>
/// → deny). The same host has already passed the SSRF guard on the main request, so no re-guard is needed.
/// </summary>
internal sealed class HttpRobotsFetcher(HttpClient client, IOptions<PolitenessOptions> options)
{
    /// <summary>A robots.txt larger than this is treated as malformed rather than buffered whole.</summary>
    internal const long MaxRobotsBytes = 512L * 1024;

    private readonly PolitenessOptions _options = options.Value;

    public async Task<RobotsPolicy.RobotsFetch> FetchAsync(Uri robotsUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(robotsUrl);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, robotsUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);

            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            // 404/410/401/403 and 5xx all read as "no usable robots" → allow (we would rather crawl than
            // guess a policy that was never served).
            if (!response.IsSuccessStatusCode)
            {
                return RobotsPolicy.RobotsFetch.NotReachable;
            }

            if (response.Content.Headers.ContentLength is { } declared && declared > MaxRobotsBytes)
            {
                return RobotsPolicy.RobotsFetch.WasMalformed;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (body.Length > MaxRobotsBytes)
            {
                return RobotsPolicy.RobotsFetch.WasMalformed;
            }

            return RobotsPolicy.RobotsFetch.Ok(body);
        }
        catch (HttpRequestException)
        {
            // The origin refused the connection or the name did not resolve: unreachable → allow.
            return RobotsPolicy.RobotsFetch.NotReachable;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A request timeout (not a caller cancellation): unreachable → allow.
            return RobotsPolicy.RobotsFetch.NotReachable;
        }
    }
}
