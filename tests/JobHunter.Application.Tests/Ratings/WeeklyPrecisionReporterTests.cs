using System.Diagnostics.Metrics;
using JobHunter.Application.Common;
using JobHunter.Application.Ratings;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Ratings;

/// <summary>
/// The weekly precision@10 reporter (F4 T20 done-when 3, D5): reads the latest opened rating round's precision
/// — the share of that week's top-ten delivered cards the Owner rated "worth opening" — and exports it as the
/// <c>jobhunter.precision_at_10</c> gauge. It records whatever the read port returns, including a measured
/// zero, so a dashboard sees the figure fall as well as rise; a week that has never been rated (a null read)
/// records nothing, because null means "not yet measured", not "measured zero". Zero collaborators touch the
/// database — the read port is faked.
/// </summary>
public sealed class WeeklyPrecisionReporterTests
{
    private readonly IWeeklyPrecisionQuery _query = Substitute.For<IWeeklyPrecisionQuery>();

    private WeeklyPrecisionReporter NewReporter() =>
        new(_query, NullLogger<WeeklyPrecisionReporter>.Instance);

    private static WeeklyPrecision Precision(int considered, int hits) =>
        new(new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero), considered, hits,
            considered == 0 ? 0m : Math.Round((decimal)hits / considered, 4));

    [Fact]
    public async Task It_exports_the_latest_weeks_precision_as_the_metric()
    {
        _query.LatestAsync(Arg.Any<CancellationToken>()).Returns(Precision(considered: 10, hits: 7));

        var recorded = await CaptureAsync(() => NewReporter().ReportAsync(CancellationToken.None));

        recorded.ShouldBe(0.7);
    }

    [Fact]
    public async Task It_records_a_measured_zero()
    {
        _query.LatestAsync(Arg.Any<CancellationToken>()).Returns(Precision(considered: 10, hits: 0));

        var recorded = await CaptureAsync(() => NewReporter().ReportAsync(CancellationToken.None));

        recorded.ShouldBe(0.0);
    }

    [Fact]
    public async Task It_records_nothing_when_no_week_has_been_rated()
    {
        _query.LatestAsync(Arg.Any<CancellationToken>()).Returns((WeeklyPrecision?)null);

        var recorded = await CaptureAsync(() => NewReporter().ReportAsync(CancellationToken.None));

        recorded.ShouldBeNull();
    }

    [Fact]
    public async Task It_returns_the_precision_it_recorded()
    {
        _query.LatestAsync(Arg.Any<CancellationToken>()).Returns(Precision(considered: 8, hits: 6));

        var result = await NewReporter().ReportAsync(CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Hits.ShouldBe(6);
        result.Considered.ShouldBe(8);
    }

    [Fact]
    public async Task It_returns_null_when_no_week_has_been_rated()
    {
        _query.LatestAsync(Arg.Any<CancellationToken>()).Returns((WeeklyPrecision?)null);

        (await NewReporter().ReportAsync(CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        Should.Throw<ArgumentNullException>(() => new WeeklyPrecisionReporter(null!, NullLogger<WeeklyPrecisionReporter>.Instance));
        Should.Throw<ArgumentNullException>(() => new WeeklyPrecisionReporter(_query, null!));
    }

    private static async Task<double?> CaptureAsync(Func<Task> act)
    {
        // Force the Telemetry static initializer to run and resolve the name up front: reading it inside the
        // InstrumentPublished callback would re-enter a type still being initialized and NRE.
        var instrumentName = Telemetry.PrecisionAtTen.Name;

        double? recorded = null;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == instrumentName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, measurement, _, _) => recorded = measurement);
        listener.Start();

        await act();

        return recorded;
    }
}
