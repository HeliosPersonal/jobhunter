using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Sources;

namespace JobHunter.Scrapers.Http;

/// <summary>
/// Maps a <see cref="GatedResponse"/> onto the domain <see cref="SourceFetch"/> the discovery handler
/// consumes (T10). It is the one place the gated HTTP outcome becomes a <see cref="FetchOutcome"/>, so
/// every adapter classifies a rate deferral, a refusal and a provider error the same way — the fetch
/// log and the quarantine counter never disagree about what happened (AC-08, AC-11).
/// </summary>
internal static class AdapterFetch
{
    public static SourceFetch From(GatedResponse response, IAsyncEnumerable<FetchedPosting> postings)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(postings);

        var (outcome, detail) = response.Outcome switch
        {
            GatedOutcome.Ok => (FetchOutcome.Success, (string?)null),
            GatedOutcome.Deferred => (FetchOutcome.RateLimited, "rate-deferred"),
            GatedOutcome.Refused when response.Reason == "robots-disallowed" =>
                (FetchOutcome.RobotsDenied, response.Reason),
            GatedOutcome.Refused => (FetchOutcome.HttpError, response.Reason),
            GatedOutcome.HttpError when response.StatusCode == 429 =>
                (FetchOutcome.RateLimited, "http-429"),
            GatedOutcome.HttpError => (FetchOutcome.HttpError, null),
            _ => (FetchOutcome.TransportError, null),
        };

        // The gated client reports a rate deferral as a synthetic 429 body; the log stores 0 for a
        // request that never produced a provider status (a refusal we made, or a transport failure).
        var httpStatus = response.Outcome switch
        {
            GatedOutcome.Ok or GatedOutcome.HttpError => (short)response.StatusCode,
            _ => (short)0,
        };

        return new SourceFetch(outcome, httpStatus, postings, response.RetryAfter, detail);
    }
}
