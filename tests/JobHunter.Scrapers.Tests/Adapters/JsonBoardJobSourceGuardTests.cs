using JobHunter.Scrapers.Adapters;
using JobHunter.Scrapers.Http;
using JobHunter.Scrapers.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Scrapers.Tests.Adapters;

/// <summary>
/// The programmer-error guards on the adapter seam. Missing collaborators are a wiring bug, so they throw
/// (coding-standards §1) rather than returning an outcome; these tests pin that contract.
/// </summary>
public sealed class JsonBoardJobSourceGuardTests
{
    private static GatedHttpClient AnyClient() =>
        new(new StubHttpClientFactory(StubHttpMessageHandler.WithBody("{\"jobs\":[]}")));

    [Fact]
    public void GatedHttpClient_rejectsNullFactory()
    {
        Should.Throw<ArgumentNullException>(() => new GatedHttpClient(null!));
    }

    [Fact]
    public void Adapter_rejectsNullHttpClient()
    {
        Should.Throw<ArgumentNullException>(
            () => new GreenhouseJobSource(null!, NullLogger<GreenhouseJobSource>.Instance));
    }

    [Fact]
    public void Adapter_rejectsNullLogger()
    {
        Should.Throw<ArgumentNullException>(() => new GreenhouseJobSource(AnyClient(), null!));
    }

    [Fact]
    public async Task Fetch_rejectsNullBinding()
    {
        var source = new GreenhouseJobSource(AnyClient(), NullLogger<GreenhouseJobSource>.Instance);

        await Should.ThrowAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in source.FetchAsync(null!, CancellationToken.None))
            {
            }
        });
    }
}
