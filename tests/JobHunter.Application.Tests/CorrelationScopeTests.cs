using System.Diagnostics;
using JobHunter.Application.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests;

public sealed class CorrelationScopeTests
{
    [Fact]
    public void Begin_keeps_a_supplied_correlation_id()
    {
        using var scope = CorrelationScope.Begin("stage.work", "corr-123", NullLogger.Instance);

        scope.CorrelationId.ShouldBe("corr-123");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Begin_generates_a_correlation_id_when_none_is_supplied(string supplied)
    {
        using var scope = CorrelationScope.Begin("stage.work", supplied, NullLogger.Instance);

        scope.CorrelationId.ShouldNotBeNullOrWhiteSpace();
        Guid.TryParse(scope.CorrelationId, out _).ShouldBeTrue();
    }

    [Fact]
    public void Begin_rejects_a_blank_operation_name()
    {
        Should.Throw<ArgumentException>(() =>
            CorrelationScope.Begin(" ", "corr", NullLogger.Instance));
    }

    [Fact]
    public void Begin_rejects_a_null_logger()
    {
        Should.Throw<ArgumentNullException>(() =>
            CorrelationScope.Begin("op", "corr", null!));
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var scope = CorrelationScope.Begin("op", "corr", NullLogger.Instance);

        scope.Dispose();
        Should.NotThrow(scope.Dispose);
    }

    [Fact]
    public void When_a_listener_is_active_the_activity_carries_the_correlation_id()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var scope = CorrelationScope.Begin("stage.work", "corr-xyz", NullLogger.Instance);

        var current = Activity.Current;
        current.ShouldNotBeNull();
        current.GetTagItem("correlation.id").ShouldBe("corr-xyz");
    }
}
