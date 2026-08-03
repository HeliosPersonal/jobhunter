using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using JobHunter.Application.Discovery;
using JobHunter.Infrastructure.Seeding;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Worker.Cli;

/// <summary>
/// The <c>seed</c> command (F1 T03): load the curated company registry from <c>tools/seed/companies.yaml</c>
/// (override with <c>--file &lt;path&gt;</c>) and upsert it. Idempotent — a second run reports zero inserts —
/// and it validates the file before touching the database, so a malformed entry aborts naming its line
/// rather than half-seeding. Excluded from coverage: the console glue; the loader and the registry service
/// are tested directly.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class SeedCommand
{
    private const string DefaultSeedPath = "tools/seed/companies.yaml";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var path = GetFileOption(args) ?? DefaultSeedPath;
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Seed file not found: {path}");
            return 1;
        }

        IReadOnlyList<CompanySeedEntry> entries;
        try
        {
            var yaml = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            entries = CompanySeedLoader.Parse(yaml);
        }
        catch (CompanySeedException ex)
        {
            // A malformed seed is an operator error surfaced by message, not a stack trace.
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        await using var scope = services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<CompanyRegistryService>();
        var change = await registry.SeedAsync(entries).ConfigureAwait(false);

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Seed complete from {path}: {change.Inserted} inserted, {change.Skipped} already present, " +
            $"{change.BindingsAdded} binding(s) recorded."));
        return 0;
    }

    private static string? GetFileOption(string[] args)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, "--file", StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
