using JobHunter.Application.Search;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Sources;
using JobHunter.TestKit;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Search;

/// <summary>
/// The operator action behind <c>POST /api/admin/sources/{id}/unquarantine</c> (F9-T07, runbook R4):
/// release a source a run of fetch failures put into quarantine, so recovery does not need a hand-written
/// UPDATE. The outcomes are values, never exceptions: an unknown id is <see cref="ReleaseOutcome.NotFound"/>
/// and does not save; a healthy source is <see cref="ReleaseOutcome.NotQuarantined"/> and does not save;
/// a genuine release lifts the aggregate's hold and persists it once.
/// </summary>
public sealed class SourceQuarantineServiceTests
{
    private readonly IJobSourceRepository _sources = Substitute.For<IJobSourceRepository>();

    private SourceQuarantineService NewService() => new(_sources);

    [Fact]
    public async Task Releasing_a_quarantined_source_lifts_the_hold_and_saves_once()
    {
        var id = Guid.NewGuid();
        var source = Quarantined(id);
        _sources.FindAsync(id, Arg.Any<CancellationToken>()).Returns(source);

        var result = await NewService().UnquarantineAsync(id);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(ReleaseOutcome.Released);
        source.QuarantinedUntil.ShouldBeNull();
        await _sources.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Releasing_a_healthy_source_reports_nothing_to_do_and_does_not_save()
    {
        var id = Guid.NewGuid();
        _sources.FindAsync(id, Arg.Any<CancellationToken>())
            .Returns(new JobSource(id, Guid.NewGuid(), Guid.NewGuid(), "https://x"));

        var result = await NewService().UnquarantineAsync(id);

        result.Value.ShouldBe(ReleaseOutcome.NotQuarantined);
        await _sources.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Releasing_an_unknown_source_is_not_found_and_does_not_save()
    {
        var id = Guid.NewGuid();
        _sources.FindAsync(id, Arg.Any<CancellationToken>()).Returns((JobSource?)null);

        var result = await NewService().UnquarantineAsync(id);

        result.Value.ShouldBe(ReleaseOutcome.NotFound);
        await _sources.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Constructor_rejects_a_null_repository() =>
        Should.Throw<ArgumentNullException>(() => new SourceQuarantineService(null!));

    private static JobSource Quarantined(Guid id)
    {
        var source = new JobSource(id, Guid.NewGuid(), Guid.NewGuid(), "https://x");
        var clock = new FakeClock();
        source.RecordFailure(clock, TimeSpan.FromHours(6));
        source.RecordFailure(clock, TimeSpan.FromHours(6));
        return source;
    }
}
