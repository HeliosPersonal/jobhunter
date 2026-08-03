using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using JobHunter.Application.Reprocessing;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Worker.Cli;

/// <summary>
/// The <c>reprocess</c> command (F2 T09, AC-09): re-run normalisation and deduplication over stored raw
/// payloads with zero network, preserving job identities where the fingerprint is unchanged so enrichments
/// and matches stay attached. Scope the run with <c>--since &lt;yyyy-MM-dd&gt;</c>; absent, it reprocesses
/// the full history. Excluded from coverage: the console glue; the <see cref="ReprocessingService"/> it
/// drives is unit-tested directly and its zero-network property is asserted there.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class ReprocessCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var since = CliDispatcher.GetSinceOption(args) ?? DateTimeOffset.MinValue;

        await using var scope = services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ReprocessingService>();
        var report = await service.ReprocessAsync(since, CancellationToken.None).ConfigureAwait(false);

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Reprocessing complete from {since:o}: {report.Examined} examined, {report.Unchanged} unchanged, " +
            $"{report.Superseded} superseded, {report.Failed} failed."));
        return 0;
    }
}
