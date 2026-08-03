using System.Net;
using System.Net.Http.Headers;
using JobHunter.Scrapers.Http;
using JobHunter.Scrapers.Tests.Support;
using Shouldly;
using Xunit;

namespace JobHunter.Scrapers.Tests.Http;

/// <summary>
/// The gated client classifies every outcome as a value (never an exception for an expected result), so a
/// robots/SSRF refusal, a rate deferral and a provider error are distinguishable by the caller. The
/// production politeness handler produces these shapes; here we feed the shapes directly.
/// </summary>
public sealed class GatedHttpClientTests
{
    private static async Task<GatedResponse> ClassifyAsync(HttpResponseMessage canned)
    {
        var handler = new StubHttpMessageHandler(_ => canned);
        var client = new GatedHttpClient(new StubHttpClientFactory(handler));
        return await client.GetAsync("https://boards-api.greenhouse.io/v1/boards/acme/jobs", CancellationToken.None);
    }

    [Fact]
    public async Task Ok_carriesTheBody()
    {
        var response = await ClassifyAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"jobs\":[]}"),
        });

        response.Outcome.ShouldBe(GatedOutcome.Ok);
        response.Body.ShouldBe("{\"jobs\":[]}");
    }

    [Fact]
    public async Task RateDeferred_isDeferred_withRetryAfter()
    {
        var canned = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            ReasonPhrase = "rate-deferred",
            Content = new StringContent(string.Empty),
        };
        canned.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));

        var response = await ClassifyAsync(canned);

        response.Outcome.ShouldBe(GatedOutcome.Deferred);
        response.RetryAfter.ShouldBe(TimeSpan.FromSeconds(120));
    }

    [Theory]
    [InlineData("robots-disallowed")]
    [InlineData("ssrf-private-address")]
    [InlineData("insecure-scheme")]
    public async Task RefusalReasons_areRefused_notHttpErrors(string reason)
    {
        var response = await ClassifyAsync(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            ReasonPhrase = reason,
            Content = new StringContent(string.Empty),
        });

        response.Outcome.ShouldBe(GatedOutcome.Refused);
        response.Reason.ShouldBe(reason);
    }

    [Fact]
    public async Task ProviderForbidden_withoutOurReason_isAnHttpError()
    {
        // A real 403 from the provider (not one of our machine reasons) is a provider fault, not a refusal.
        var response = await ClassifyAsync(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            ReasonPhrase = "Forbidden",
            Content = new StringContent(string.Empty),
        });

        response.Outcome.ShouldBe(GatedOutcome.HttpError);
        response.StatusCode.ShouldBe(403);
    }

    [Fact]
    public async Task ServerError_isAnHttpError()
    {
        var response = await ClassifyAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom"),
        });

        response.Outcome.ShouldBe(GatedOutcome.HttpError);
        response.StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task RateDeferred_withoutRetryAfterHeader_hasNullDelay()
    {
        var response = await ClassifyAsync(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            ReasonPhrase = "rate-deferred",
            Content = new StringContent(string.Empty),
        });

        response.Outcome.ShouldBe(GatedOutcome.Deferred);
        response.RetryAfter.ShouldBeNull();
    }
}
