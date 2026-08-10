using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Sources;
using JobHunter.Scrapers.Http;
using Shouldly;
using Xunit;

namespace JobHunter.Scrapers.Tests.Http;

/// <summary>
/// The one place a gated HTTP outcome becomes a domain <see cref="SourceFetch"/> (T10, AC-08, AC-11). Every
/// adapter routes through it, so the classification must be exhaustive and unambiguous: a rate deferral, a
/// robots refusal, an SSRF/scheme refusal, a 429 provider error and any other provider error each map to one
/// <see cref="FetchOutcome"/>, and the logged HTTP status is a provider status only when the provider actually
/// answered (Ok / HttpError) — a refusal we made or a transport failure logs 0, so the fetch log and the
/// quarantine counter never disagree about what happened.
/// </summary>
public sealed class AdapterFetchTests
{
    private static readonly IAsyncEnumerable<FetchedPosting> NoPostings = Empty();

    [Fact]
    public void Ok_maps_to_success_with_the_provider_status_and_no_detail()
    {
        var fetch = AdapterFetch.From(GatedResponse.Ok(200, "{}"), NoPostings);

        fetch.Outcome.ShouldBe(FetchOutcome.Success);
        fetch.HttpStatus.ShouldBe((short)200);
        fetch.Detail.ShouldBeNull();
        fetch.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Deferred_maps_to_rate_limited_and_logs_zero_status()
    {
        var retryAfter = TimeSpan.FromSeconds(30);
        var fetch = AdapterFetch.From(GatedResponse.Deferred(retryAfter), NoPostings);

        fetch.Outcome.ShouldBe(FetchOutcome.RateLimited);
        fetch.Detail.ShouldBe("rate-deferred");
        // A deferral is our decision, not a provider status, so the log stores 0.
        fetch.HttpStatus.ShouldBe((short)0);
        fetch.RetryAfter.ShouldBe(retryAfter);
    }

    [Fact]
    public void Refused_for_robots_maps_to_robots_denied_and_carries_the_reason()
    {
        var fetch = AdapterFetch.From(GatedResponse.Refused("robots-disallowed"), NoPostings);

        fetch.Outcome.ShouldBe(FetchOutcome.RobotsDenied);
        fetch.Detail.ShouldBe("robots-disallowed");
        fetch.HttpStatus.ShouldBe((short)0);
    }

    [Fact]
    public void Refused_for_any_other_reason_maps_to_http_error_and_carries_the_reason()
    {
        // An SSRF or insecure-scheme refusal is still a refusal we made, so no provider status is logged.
        var fetch = AdapterFetch.From(GatedResponse.Refused("ssrf-blocked"), NoPostings);

        fetch.Outcome.ShouldBe(FetchOutcome.HttpError);
        fetch.Detail.ShouldBe("ssrf-blocked");
        fetch.HttpStatus.ShouldBe((short)0);
    }

    [Fact]
    public void HttpError_429_maps_to_rate_limited_with_the_provider_status()
    {
        var fetch = AdapterFetch.From(GatedResponse.HttpError(429), NoPostings);

        fetch.Outcome.ShouldBe(FetchOutcome.RateLimited);
        fetch.Detail.ShouldBe("http-429");
        // A 429 is a real provider status, so it is logged as one.
        fetch.HttpStatus.ShouldBe((short)429);
    }

    [Fact]
    public void HttpError_other_than_429_maps_to_http_error_with_the_provider_status_and_no_detail()
    {
        var fetch = AdapterFetch.From(GatedResponse.HttpError(503), NoPostings);

        fetch.Outcome.ShouldBe(FetchOutcome.HttpError);
        fetch.Detail.ShouldBeNull();
        fetch.HttpStatus.ShouldBe((short)503);
    }

    [Fact]
    public void An_unrecognised_outcome_maps_to_transport_error_and_logs_zero_status()
    {
        // A synthesised outcome that is none of the classified cases falls through to the default arm.
        var response = new GatedResponse((GatedOutcome)999, 500, null, null, null);

        var fetch = AdapterFetch.From(response, NoPostings);

        fetch.Outcome.ShouldBe(FetchOutcome.TransportError);
        fetch.Detail.ShouldBeNull();
        fetch.HttpStatus.ShouldBe((short)0);
    }

    private static async IAsyncEnumerable<FetchedPosting> Empty()
    {
        await Task.CompletedTask;
        yield break;
    }
}
