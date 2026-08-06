using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using JobHunter.Application.Preferences;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Worker.Cli;

/// <summary>
/// The <c>backfill-signals</c> command (F7 T03, done-when 5): replay the application outcomes recorded before
/// F6 began staging signals into signals, so the preference fitter sees the whole history and not only what
/// happened after the feature shipped. Scope the run with <c>--since &lt;yyyy-MM-dd&gt;</c>; absent, it
/// replays the full history. Idempotent — a second run, over an already-migrated history, captures nothing
/// more and reports every outcome as already present. Excluded from coverage: the console glue; the
/// <see cref="SignalBackfillService"/> it drives is unit-tested directly and its idempotence is asserted there.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class BackfillSignalsCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var since = CliDispatcher.GetSinceOption(args) ?? DateTimeOffset.MinValue;

        await using var scope = services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<SignalBackfillService>();
        var report = await service.BackfillAsync(since, CancellationToken.None).ConfigureAwait(false);

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Signal backfill complete from {since:o}: {report.Examined} examined, {report.Captured} captured, " +
            $"{report.Skipped} already present, {report.WithoutFacts} without live facts."));
        return 0;
    }
}
