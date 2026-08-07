using System.Net;
using JobHunter.Infrastructure.Http;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Http;

/// <summary>
/// The connection half of the research SSRF defence (SAD §11 D1, QG-3): "resolve once, connect to the
/// resolved address." As the research client's <c>ConnectCallback</c> it runs when a socket is opened —
/// after every earlier check and after any redirect — so it is the last line that defeats DNS rebinding: a
/// name that answered public to an earlier validation but resolves private at connect time gets no socket.
/// It resolves the host <em>once</em>, refuses unless every resolved address is public, and dials the exact
/// address it validated. Every refusal case asserts <em>no dial was made</em> — the request never happened.
/// </summary>
public sealed class ResearchConnectorTests
{
    private const int Port = 443;

    private static SsrfGuard.ResolveHost Resolves(params string[] ips) =>
        (_, _) => Task.FromResult<IReadOnlyList<IPAddress>>(ips.Select(IPAddress.Parse).ToArray());

    [Fact]
    public async Task A_public_name_is_dialed_at_the_address_it_resolved_to()
    {
        IPAddress? dialed = null;
        var connector = new ResearchConnector(
            Resolves("93.184.216.34"),
            (address, _, _) => { dialed = address; return Task.FromResult<Stream>(new MemoryStream()); });

        await using var stream = await connector.ConnectAsync("example.com", Port, CancellationToken.None);

        dialed.ShouldBe(IPAddress.Parse("93.184.216.34"));
    }

    [Fact]
    public async Task A_name_resolving_to_a_private_address_is_refused_without_dialing()
    {
        var dialCalls = 0;
        var connector = new ResearchConnector(
            Resolves("10.0.0.5"),
            (_, _, _) => { dialCalls++; return Task.FromResult<Stream>(new MemoryStream()); });

        await Should.ThrowAsync<HttpRequestException>(() => connector.ConnectAsync("rebind.example", Port, CancellationToken.None));
        dialCalls.ShouldBe(0);
    }

    [Fact]
    public async Task A_name_resolving_to_any_private_address_is_refused_even_if_others_are_public()
    {
        var dialCalls = 0;
        var connector = new ResearchConnector(
            Resolves("93.184.216.34", "192.168.1.9"),
            (_, _, _) => { dialCalls++; return Task.FromResult<Stream>(new MemoryStream()); });

        await Should.ThrowAsync<HttpRequestException>(() => connector.ConnectAsync("mixed.example", Port, CancellationToken.None));
        dialCalls.ShouldBe(0);
    }

    [Fact]
    public async Task A_name_that_does_not_resolve_is_refused_without_dialing()
    {
        var dialCalls = 0;
        var connector = new ResearchConnector(
            (_, _) => Task.FromResult<IReadOnlyList<IPAddress>>([]),
            (_, _, _) => { dialCalls++; return Task.FromResult<Stream>(new MemoryStream()); });

        await Should.ThrowAsync<HttpRequestException>(() => connector.ConnectAsync("nx.invalid", Port, CancellationToken.None));
        dialCalls.ShouldBe(0);
    }

    [Fact]
    public async Task A_public_ip_literal_is_dialed_directly_without_resolving()
    {
        var resolverCalls = 0;
        IPAddress? dialed = null;
        var connector = new ResearchConnector(
            (_, _) => { resolverCalls++; return Task.FromResult<IReadOnlyList<IPAddress>>([]); },
            (address, _, _) => { dialed = address; return Task.FromResult<Stream>(new MemoryStream()); });

        await using var stream = await connector.ConnectAsync("8.8.8.8", Port, CancellationToken.None);

        dialed.ShouldBe(IPAddress.Parse("8.8.8.8"));
        resolverCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    public async Task A_private_ip_literal_is_refused_without_resolving_or_dialing(string ip)
    {
        var resolverCalls = 0;
        var dialCalls = 0;
        var connector = new ResearchConnector(
            (_, _) => { resolverCalls++; return Task.FromResult<IReadOnlyList<IPAddress>>([]); },
            (_, _, _) => { dialCalls++; return Task.FromResult<Stream>(new MemoryStream()); });

        await Should.ThrowAsync<HttpRequestException>(() => connector.ConnectAsync(ip, Port, CancellationToken.None));
        resolverCalls.ShouldBe(0);
        dialCalls.ShouldBe(0);
    }

    [Fact]
    public async Task The_host_is_resolved_exactly_once_and_the_dial_uses_that_resolution()
    {
        // "Resolve once, connect to the resolved address": the connection performs a single resolution and
        // dials its answer. A second, connect-time resolution is where rebinding would substitute a private
        // address; there is none.
        var resolverCalls = 0;
        IPAddress? dialed = null;
        var connector = new ResearchConnector(
            (_, _) => { resolverCalls++; return Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("93.184.216.34")]); },
            (address, _, _) => { dialed = address; return Task.FromResult<Stream>(new MemoryStream()); });

        await using var stream = await connector.ConnectAsync("once.example", Port, CancellationToken.None);

        resolverCalls.ShouldBe(1);
        dialed.ShouldBe(IPAddress.Parse("93.184.216.34"));
    }
}
