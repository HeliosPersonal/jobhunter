using System.Net;
using JobHunter.Infrastructure.Http;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Http;

public sealed class SsrfGuardTests
{
    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("127.0.0.1")]
    [InlineData("127.1.2.3")]
    [InlineData("169.254.0.1")]
    [InlineData("169.254.169.254")] // the cloud metadata endpoint
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    public void Rejects_private_loopback_and_linklocal_v4(string ip)
    {
        SsrfGuard.IsPublic(IPAddress.Parse(ip)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.0.1")] // just outside 172.16/12
    [InlineData("172.32.0.1")] // just outside 172.16/12
    [InlineData("192.167.0.1")] // just outside 192.168/16
    [InlineData("11.0.0.1")]
    public void Accepts_public_v4(string ip)
    {
        SsrfGuard.IsPublic(IPAddress.Parse(ip)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("::1")] // loopback
    [InlineData("fe80::1")] // link-local
    [InlineData("fc00::1")] // unique-local
    [InlineData("fd00::1")] // unique-local
    [InlineData("::")] // unspecified
    [InlineData("ff02::1")] // multicast
    [InlineData("::ffff:10.0.0.1")] // v4-mapped private
    public void Rejects_non_public_v6(string ip)
    {
        SsrfGuard.IsPublic(IPAddress.Parse(ip)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("2606:4700:4700::1111")] // Cloudflare
    [InlineData("::ffff:8.8.8.8")] // v4-mapped public
    public void Accepts_public_v6(string ip)
    {
        SsrfGuard.IsPublic(IPAddress.Parse(ip)).ShouldBeTrue();
    }

    [Fact]
    public async Task IsPublicAsync_classifies_an_ip_literal_without_resolving()
    {
        var resolverCalled = false;
        var guard = new SsrfGuard((_, _) =>
        {
            resolverCalled = true;
            return Task.FromResult<IReadOnlyList<IPAddress>>([]);
        });

        (await guard.IsPublicAsync(new Uri("https://8.8.8.8/jobs"))).ShouldBeTrue();
        (await guard.IsPublicAsync(new Uri("https://10.0.0.1/jobs"))).ShouldBeFalse();
        resolverCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task IsPublicAsync_refuses_when_any_resolved_address_is_private()
    {
        var guard = new SsrfGuard((_, _) => Task.FromResult<IReadOnlyList<IPAddress>>(
            [IPAddress.Parse("8.8.8.8"), IPAddress.Parse("10.0.0.5")]));

        (await guard.IsPublicAsync(new Uri("https://rebind.example/jobs"))).ShouldBeFalse();
    }

    [Fact]
    public async Task IsPublicAsync_accepts_when_all_resolved_addresses_are_public()
    {
        var guard = new SsrfGuard((_, _) => Task.FromResult<IReadOnlyList<IPAddress>>(
            [IPAddress.Parse("8.8.8.8"), IPAddress.Parse("1.1.1.1")]));

        (await guard.IsPublicAsync(new Uri("https://boards.example/jobs"))).ShouldBeTrue();
    }

    [Fact]
    public async Task IsPublicAsync_refuses_a_host_that_does_not_resolve()
    {
        var guard = new SsrfGuard((_, _) =>
            throw new System.Net.Sockets.SocketException(11001));

        (await guard.IsPublicAsync(new Uri("https://nx.invalid/jobs"))).ShouldBeFalse();
    }

    [Fact]
    public async Task IsPublicAsync_refuses_a_host_that_resolves_to_nothing()
    {
        var guard = new SsrfGuard((_, _) => Task.FromResult<IReadOnlyList<IPAddress>>([]));

        (await guard.IsPublicAsync(new Uri("https://empty.example/jobs"))).ShouldBeFalse();
    }
}
