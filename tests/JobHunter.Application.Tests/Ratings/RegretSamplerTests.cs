using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using JobHunter.Application.Common;
using JobHunter.Application.Ratings;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Ratings;

/// <summary>
/// F4 T21 (ADR-F4-0003): the regret sampler. Each week it samples the latest Run's pre-match-excluded jobs,
/// matches them at the cheap tier, records how many would have scored at or above the presentation threshold to
/// <c>jobhunter.matching.regret</c>, and alerts the Owner when any did — a non-zero regret means a filter rule
/// is wrong. The sample runs once per week, gated by <see cref="IRegretSampleLog"/>: a redelivered tick samples
/// nothing, spends nothing and raises no duplicate alert.
/// </summary>
public sealed class RegretSamplerTests
{
    private static readonly DateTimeOffset WeekStart = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
    private const long OwnerChatId = 4242;

    [Fact]
    public async Task It_alerts_and_records_regret_when_a_sampled_job_would_have_scored_above_threshold()
    {
        var above = JobId(1);
        var below = JobId(2);
        var matcher = new FakeRegretMatcher
        {
            Scores =
            {
                [above] = 55m,   // above the 40 presentation threshold — a regret
                [below] = 12m,   // the filter was right about this one
            },
        };
        var notifier = new CapturingNotifier();
        var sampler = NewSampler(matcher, notifier, new FakeRegretSampleLog(), sample: [Content(above), Content(below)]);

        var captured = await CaptureAsync(() => sampler.SampleAsync(WeekStart));

        captured.ShouldContain(1L);
        notifier.Sent.Count.ShouldBe(1);
        notifier.Sent[0].ChatId.ShouldBe(OwnerChatId);
        notifier.Sent[0].Message.Text.ShouldContain("1");
    }

    [Fact]
    public async Task It_records_zero_and_does_not_alert_when_every_sampled_job_scores_below_threshold()
    {
        var job = JobId(1);
        var matcher = new FakeRegretMatcher { Scores = { [job] = 20m } };
        var notifier = new CapturingNotifier();
        var sampler = NewSampler(matcher, notifier, new FakeRegretSampleLog(), sample: [Content(job)]);

        var captured = await CaptureAsync(() => sampler.SampleAsync(WeekStart));

        // A measured zero is still recorded, so a dashboard sees regret stay flat rather than go blank.
        captured.ShouldContain(0L);
        notifier.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_job_exactly_on_the_threshold_counts_as_regret()
    {
        var job = JobId(1);
        var matcher = new FakeRegretMatcher { Scores = { [job] = 40m } };
        var notifier = new CapturingNotifier();
        var sampler = NewSampler(matcher, notifier, new FakeRegretSampleLog(), sample: [Content(job)]);

        var captured = await CaptureAsync(() => sampler.SampleAsync(WeekStart));

        captured.ShouldContain(1L);
        notifier.Sent.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_empty_sample_records_zero_and_sends_nothing_without_calling_the_matcher()
    {
        var matcher = new FakeRegretMatcher();
        var notifier = new CapturingNotifier();
        var sampler = NewSampler(matcher, notifier, new FakeRegretSampleLog(), sample: []);

        var captured = await CaptureAsync(() => sampler.SampleAsync(WeekStart));

        captured.ShouldContain(0L);
        notifier.Sent.ShouldBeEmpty();
        matcher.Called.ShouldBeFalse();
    }

    [Fact]
    public async Task A_week_already_sampled_samples_nothing_and_spends_nothing()
    {
        var job = JobId(1);
        var log = new FakeRegretSampleLog();
        await log.TryOpenAsync(WeekStart, WeekStart);   // the week was already opened by an earlier tick
        var matcher = new FakeRegretMatcher { Scores = { [job] = 90m } };
        var query = Substitute.For<IFilterExcludedSampleQuery>();
        var notifier = new CapturingNotifier();
        var sampler = new RegretSampler(
            query, matcher, log, notifier, DeliveryOptionsFor(OwnerChatId), NullLogger<RegretSampler>.Instance);

        await sampler.SampleAsync(WeekStart);

        matcher.Called.ShouldBeFalse();
        notifier.Sent.ShouldBeEmpty();
        // The query is never even asked: a redelivered tick short-circuits on the idempotence gate.
        await query.DidNotReceive().SampleAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task It_samples_exactly_twenty_excluded_jobs()
    {
        var query = Substitute.For<IFilterExcludedSampleQuery>();
        query.SampleAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        var sampler = new RegretSampler(
            query, new FakeRegretMatcher(), new FakeRegretSampleLog(), new CapturingNotifier(),
            DeliveryOptionsFor(OwnerChatId), NullLogger<RegretSampler>.Instance);

        await sampler.SampleAsync(WeekStart);

        await query.Received(1).SampleAsync(20, Arg.Any<CancellationToken>());
    }

    private static RegretSampler NewSampler(
        FakeRegretMatcher matcher,
        CapturingNotifier notifier,
        FakeRegretSampleLog log,
        IReadOnlyList<MatchJobContent> sample)
    {
        var query = Substitute.For<IFilterExcludedSampleQuery>();
        query.SampleAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(sample);
        return new RegretSampler(
            query, matcher, log, notifier, DeliveryOptionsFor(OwnerChatId), NullLogger<RegretSampler>.Instance);
    }

    private static JobHunter.Application.Delivery.DeliveryOptions DeliveryOptionsFor(long chatId) =>
        new() { OwnerChatId = chatId };

    private static Guid JobId(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    private static MatchJobContent Content(Guid jobId) =>
        new(jobId, "Acme", "acme.com", "Staff SRE", "Staff", "Remote", null, "FullTime", "desc", Enrichment: null);

    /// <summary>Runs <paramref name="act"/> with a listener on <c>jobhunter.matching.regret</c>, returning every recorded value.</summary>
    private static async Task<IReadOnlyList<long>> CaptureAsync(Func<Task> act)
    {
        var measurements = new ConcurrentBag<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == Telemetry.MeterName && instrument.Name == Telemetry.MatchingRegret.Name)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => measurements.Add(value));
        listener.Start();

        await act();

        listener.Dispose();
        return measurements.ToList();
    }

    private sealed class FakeRegretMatcher : IRegretMatcher
    {
        public Dictionary<Guid, decimal> Scores { get; } = new();

        public bool Called { get; private set; }

        public Task<IReadOnlyList<RegretMatch>> MatchAsync(
            IReadOnlyList<MatchJobContent> jobs, CancellationToken cancellationToken = default)
        {
            Called = true;
            IReadOnlyList<RegretMatch> result = jobs
                .Where(j => Scores.ContainsKey(j.JobId))
                .Select(j => new RegretMatch(j.JobId, Scores[j.JobId]))
                .ToList();
            return Task.FromResult(result);
        }
    }

    private sealed class FakeRegretSampleLog : IRegretSampleLog
    {
        private readonly ConcurrentDictionary<DateTimeOffset, byte> _weeks = new();

        public Task<bool> TryOpenAsync(
            DateTimeOffset weekStart, DateTimeOffset openedAt, CancellationToken cancellationToken = default) =>
            Task.FromResult(_weeks.TryAdd(weekStart, 0));
    }

    private sealed class CapturingNotifier : INotifier
    {
        private readonly List<(long ChatId, RenderedMessage Message)> _sent = [];

        public List<(long ChatId, RenderedMessage Message)> Sent => _sent;

        public Task<long> SendAsync(long chatId, RenderedMessage message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);
            _sent.Add((chatId, message));
            return Task.FromResult(1L);
        }
    }
}
