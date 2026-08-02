using JobHunter.Worker;
using JobHunter.Worker.Cli;

// The Worker is both a long-running host and the home of the operational CLI (SAD §5). A recognised
// verb (migrate, replay-dlq) runs the command and exits; no verb starts the pipeline host.
if (CliDispatcher.TryGetCommand(args, out var command))
{
    return await CliDispatcher.RunAsync(command!.Value, args);
}

await WorkerHost.CreateHost(args).RunAsync();
return 0;
