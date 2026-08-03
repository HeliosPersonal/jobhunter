using JobHunter.Application.Normalization;
using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Normalization;

public sealed class RemotePolicyResolverTests
{
    [Theory]
    [InlineData(RemotePolicy.Remote)]
    [InlineData(RemotePolicy.Hybrid)]
    [InlineData(RemotePolicy.Onsite)]
    public void An_explicit_signal_always_wins(RemotePolicy signal)
    {
        // Even location text that would infer otherwise is overridden by the provider's own field.
        RemotePolicyResolver.Resolve(signal, "Berlin, Germany").ShouldBe(signal);
    }

    [Fact]
    public void No_signal_and_no_text_is_unknown()
    {
        RemotePolicyResolver.Resolve(null, null).ShouldBe(RemotePolicy.Unknown);
        RemotePolicyResolver.Resolve(null, "  ").ShouldBe(RemotePolicy.Unknown);
    }

    [Fact]
    public void A_named_place_with_no_remote_wording_is_onsite()
    {
        RemotePolicyResolver.Resolve(null, "Berlin, Germany").ShouldBe(RemotePolicy.Onsite);
    }

    [Fact]
    public void Hybrid_wording_is_hybrid()
    {
        RemotePolicyResolver.Resolve(null, "Hybrid - Berlin").ShouldBe(RemotePolicy.Hybrid);
    }

    [Fact]
    public void Global_remote_is_remote()
    {
        RemotePolicyResolver.Resolve(null, "Remote").ShouldBe(RemotePolicy.Remote);
        RemotePolicyResolver.Resolve(null, "Anywhere").ShouldBe(RemotePolicy.Remote);
        RemotePolicyResolver.Resolve(null, "Remote - Worldwide").ShouldBe(RemotePolicy.Remote);
    }

    [Fact]
    public void Regionally_scoped_remote_is_remote_regional()
    {
        RemotePolicyResolver.Resolve(null, "Remote - EMEA").ShouldBe(RemotePolicy.RemoteRegional);
        RemotePolicyResolver.Resolve(null, "Remote (US)").ShouldBe(RemotePolicy.RemoteRegional);
        RemotePolicyResolver.Resolve(null, "Remote within Europe").ShouldBe(RemotePolicy.RemoteRegional);
    }

    [Fact]
    public void Work_from_home_is_treated_as_remote()
    {
        RemotePolicyResolver.Resolve(null, "Work from home").ShouldBe(RemotePolicy.Remote);
    }
}
