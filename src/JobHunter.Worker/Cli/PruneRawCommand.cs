using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using JobHunter.Application.Reprocessing;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Worker.Cli;

/// <summary>
/// The <c>prune-raw</c> command (F2 T09, O3): delete raw postings gone cold for longer than the retention
/// window (90 days), never removing one still referenced by a live or closed job's provenance. Excluded
/// from coverage: the console glue; the <see cref="RetentionService"/> and the repository prune are tested
/// directly, including the guarantee that a referenced posting is never deleted.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class PruneRawCommand
{
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<RetentionService>();
        var pruned = await service.PruneAsync(RetentionService.DefaultRetention, CancellationToken.None).ConfigureAwait(false);

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Retention prune complete: {pruned} raw posting(s) removed."));
        return 0;
    }
}
