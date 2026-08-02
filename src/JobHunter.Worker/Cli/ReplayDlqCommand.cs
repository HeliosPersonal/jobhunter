using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using JobHunter.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Worker.Cli;

/// <summary>
/// The <c>replay-dlq</c> command (T16, runbook R6). <c>--list</c> prints dead-lettered messages grouped
/// by stage; <c>--queue &lt;name&gt;</c> re-enqueues a dead-letter queue onto its source. Replay is safe
/// against the consumer inbox (invariant 8). Excluded from coverage — the console glue; the replay logic
/// and the argument parsing are tested directly (<see cref="RabbitMqDeadLetterReplayer"/>, dispatcher).
/// </summary>
[ExcludeFromCodeCoverage]
internal static class ReplayDlqCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var replayer = services.GetRequiredService<IDeadLetterReplayer>();

        var queue = CliDispatcher.GetQueueOption(args);
        if (queue is not null)
        {
            var result = await replayer.ReplayQueueAsync(queue).ConfigureAwait(false);
            Console.WriteLine(Describe(result, queue));
            return result.Outcome == ReplayOutcome.Replayed ? 0 : 1;
        }

        // Default and explicit --list both list.
        var summaries = await replayer.ListAsync().ConfigureAwait(false);
        if (summaries.Count == 0)
        {
            Console.WriteLine("No dead-lettered messages.");
            return 0;
        }

        Console.WriteLine("Dead-lettered messages by queue:");
        foreach (var summary in summaries)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {summary.DeadLetterQueue}  ->  {summary.SourceQueue}  ({summary.MessageCount})"));
        }

        return 0;
    }

    private static string Describe(ReplayResult result, string queue) => result.Outcome switch
    {
        ReplayOutcome.Replayed => string.Create(
            CultureInfo.InvariantCulture, $"Re-enqueued {result.MovedCount} message(s) from {queue}."),
        _ => result.Message ?? $"Nothing replayed from {queue}.",
    };
}
