using System.Diagnostics.CodeAnalysis;
using JobHunter.Application;
using JobHunter.Infrastructure;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JobHunter.Worker.Cli;

/// <summary>
/// Recognises an operational verb on the command line and runs it (T16, SAD §5). Argument parsing is
/// pure and unit-tested via <see cref="TryGetCommand"/>; the side-effecting run paths compose a minimal
/// host and are covered by integration tests.
/// </summary>
public static class CliDispatcher
{
    private static readonly Dictionary<string, CliCommand> Verbs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["migrate"] = CliCommand.Migrate,
        ["replay-dlq"] = CliCommand.ReplayDlq,
        ["seed"] = CliCommand.Seed,
    };

    /// <summary>
    /// True when the first non-flag argument is a recognised verb. Pure — no side effects — so the
    /// mapping is unit-tested without a host.
    /// </summary>
    public static bool TryGetCommand(string[] args, out CliCommand? command)
    {
        ArgumentNullException.ThrowIfNull(args);

        var verb = args.FirstOrDefault(a => !a.StartsWith('-'));
        if (verb is not null && Verbs.TryGetValue(verb, out var parsed))
        {
            command = parsed;
            return true;
        }

        command = null;
        return false;
    }

    /// <summary>Parses a <c>--queue &lt;name&gt;</c> option, if present.</summary>
    public static string? GetQueueOption(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var index = Array.FindIndex(args, a => string.Equals(a, "--queue", StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>True when <c>--list</c> is present.</summary>
    public static bool HasListFlag(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Any(a => string.Equals(a, "--list", StringComparison.OrdinalIgnoreCase));
    }

    [ExcludeFromCodeCoverage]
    public static async Task<int> RunAsync(CliCommand command, string[] args)
    {
        using var host = BuildCliHost(args);
        return command switch
        {
            CliCommand.Migrate => await MigrateCommand.RunAsync(host.Services),
            CliCommand.ReplayDlq => await ReplayDlqCommand.RunAsync(host.Services, args),
            CliCommand.Seed => await SeedCommand.RunAsync(host.Services, args),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unhandled CLI command."),
        };
    }

    [ExcludeFromCodeCoverage]
    private static IHost BuildCliHost(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.AddEnvVariablesAndConfigureSecrets();
        builder.Services.AddJobHunterApplication();
        builder.Services.AddJobHunterInfrastructure(builder.Configuration);
        builder.Services.AddDeadLetterReplay();

        return builder.Build();
    }
}
