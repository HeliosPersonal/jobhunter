using System.Diagnostics.Metrics;
using JobHunter.Application.Common;
using JobHunter.Application.Preferences;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Preferences;

/// <summary>
/// The suppression-regret reporter (F7 T09 done-when 5, risk D3): reads how many of the latest Run's
/// suppressed jobs the Owner acted on and exports it as <c>jobhunter.preferences.suppression_regret</c>, the
/// counterweight to precision@10 — a rising regret is the signal the learned model is over-suppressing
/// (invariant 11). It records whatever the read port returns, including zero, so a dashboard sees regret fall
/// as well as rise, and reports the value it recorded.
/// </summary>
public sealed class SuppressionRegretReporterTests
{
    private readonly ISuppressionRegretQuery _query = Substitute.For<ISuppressionRegretQuery>();

    private SuppressionRegretReporter NewReporter() =>
        new(_query, NullLogger<SuppressionRegretReporter>.Instance);

    [Fact]
    public async Task It_exports_the_regret_count_as_the_metric()
    {
        _query.LatestRunRegretCountAsync(Arg.Any<CancellationToken>()).Returns(3);

        var recorded = await CaptureAsync(() => NewReporter().ReportAsync(CancellationToken.None));

        recorded.ShouldBe(3L);
    }

    [Fact]
    public async Task It_records_zero_when_there_is_no_regret()
    {
        _query.LatestRunRegretCountAsync(Arg.Any<CancellationToken>()).Returns(0);

        var recorded = await CaptureAsync(() => NewReporter().ReportAsync(CancellationToken.None));

        recorded.ShouldBe(0L);
    }

    [Fact]
    public async Task It_returns_the_value_it_recorded()
    {
        _query.LatestRunRegretCountAsync(Arg.Any<CancellationToken>()).Returns(5);

        (await NewReporter().ReportAsync(CancellationToken.None)).ShouldBe(5);
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        Should.Throw<ArgumentNullException>(() => new SuppressionRegretReporter(null!, NullLogger<SuppressionRegretReporter>.Instance));
        Should.Throw<ArgumentNullException>(() => new SuppressionRegretReporter(_query, null!));
    }

    private static async Task<long?> CaptureAsync(Func<Task> act)
    {
        // Force the Telemetry static initializer to run and resolve the name up front: reading it inside the
        // InstrumentPublished callback would re-enter a type still being initialized and NRE.
        var instrumentName = Telemetry.SuppressionRegret.Name;

        long? recorded = null;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == instrumentName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => recorded = measurement);
        listener.Start();

        await act();

        return recorded;
    }
}
