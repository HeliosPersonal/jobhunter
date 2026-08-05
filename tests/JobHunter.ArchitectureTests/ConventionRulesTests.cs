using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using Microsoft.EntityFrameworkCore;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace JobHunter.ArchitectureTests;

/// <summary>
/// The convention rules from coding-standards §2 that are about shape and idiom rather than layering:
/// Dapper never writes (rule 4), only <c>SystemClock</c> reads the ambient clock (rule 5), nothing
/// outside <c>src/Aspire</c> references the AppHost (rule 6), every entity configuration is
/// <c>internal sealed</c> (rule 7), and Infrastructure exposes only extension/options types (rule 8).
/// </summary>
public sealed class ConventionRulesTests
{
    [Fact]
    public void Rule4_DapperQueries_neverWrite()
    {
        // NetArchTest 1.3.2 has no call-graph assertion, so this rule is enforced at the source level:
        // no type in the Queries namespace may name a Dapper write method.
        var writes = SourceScan
            .ForPattern(@"\.(ExecuteAsync|Execute|ExecuteScalar|ExecuteScalarAsync)\b")
            .ExcludingFiles() // scan the whole tree; only Queries files would legitimately trip it
            .Matches
            .Where(m => m.Contains("Query", StringComparison.OrdinalIgnoreCase))
            .ToList();

        writes.ShouldBeEmpty("Dapper read queries must never call a write method: " + string.Join("; ", writes));
    }

    [Fact]
    public void Rule5_onlySystemClock_readsTheAmbientClock()
    {
        var matches = SourceScan
            .ForPattern(@"DateTime(Offset)?\.(Now|UtcNow)")
            .ExcludingType<JobHunter.Domain.Abstractions.SystemClock>()
            .Matches;

        matches.ShouldBeEmpty(
            "Only SystemClock may read the ambient clock (architecture rule 5): " + string.Join("; ", matches));
    }

    [Fact]
    public void Rule6_nothingOutsideAspire_referencesTheAppHost()
    {
        // The AppHost assembly is not referenced by any production project, so it is not even loadable
        // from here. Assert its types never appear as a dependency of the layered assemblies.
        var result = Types.InAssemblies(
            [
                typeof(JobHunter.Domain.Abstractions.IClock).Assembly,
                typeof(JobHunter.Application.Common.Telemetry).Assembly,
                typeof(JobHunter.Infrastructure.DependencyInjection).Assembly,
            ])
            .Should()
            .NotHaveDependencyOn("JobHunter.AppHost")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(LayeringRulesTests.FailureMessage(result));
    }

    [Fact]
    public void Rule7_everyEntityConfiguration_isInternalSealed()
    {
        var result = Types.InAssembly(typeof(JobHunterDbContext).Assembly)
            .That()
            .ImplementInterface(typeof(IEntityTypeConfiguration<>))
            .Should()
            .BeSealed()
            .And()
            .NotBePublic()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(LayeringRulesTests.FailureMessage(result));
    }

    [Fact]
    public void QG2_noScraperAdapter_constructsItsOwnHttpClient()
    {
        // QG-2 (SAD §8): an ATS adapter must be handed the shared, politeness-gated HttpClient and can
        // never build its own — that is what makes robots/rate/SSRF/size limits structural rather than a
        // convention each adapter author must remember. The gated pipeline lives in Infrastructure/Http;
        // Scrapers gets a client by name and constructs no HttpClient, SocketsHttpHandler or
        // HttpClientHandler of its own.
        var offenders = SourceScan
            .ForPattern(@"new\s+(HttpClient|SocketsHttpHandler|HttpClientHandler|HttpMessageInvoker)\b")
            .InDirectory(ScrapersSourceDirectory())
            .Matches;

        offenders.ShouldBeEmpty(
            "No type in JobHunter.Scrapers may construct its own HTTP client (QG-2): " + string.Join("; ", offenders));
    }

    [Fact]
    public void Rule8_public_infrastructureTypes_areExtensionOrOptionsOrPorts()
    {
        // A public Infrastructure type must be a DI extension class, an *Options class, a *Factory, a
        // *Configuration helper, a repository/query seam, or an *Attribute — never a leaked service.
        var offenders = Types.InAssembly(typeof(JobHunter.Infrastructure.DependencyInjection).Assembly)
            .That()
            .ArePublic()
            .And()
            .AreNotAbstract()
            .Should()
            .MeetCustomRule(new PublicInfrastructureTypeRule())
            .GetResult();

        offenders.IsSuccessful.ShouldBeTrue(LayeringRulesTests.FailureMessage(offenders));
    }

    [Fact]
    public void Rule9_noFormatter_interpolatesARawValueIntoActiveMarkup()
    {
        // F5 message contract §Escaping: a message formatter must never interpolate a non-constant straight
        // into MarkdownV2 markup — the value has to pass through MarkdownV2Escaper.Escape and the markup be
        // added as an adjacent constant, or one unescaped special silently fails the whole send. The hazard
        // is specifically an interpolation hole sitting next to active markup (e.g. $"*{title}*"), which
        // this scan of the Telegram Formatting tree forbids.
        var offenders = SourceScan
            .ForPattern(@"\$""[^""]*([*_\[\]`~|]\{|\}[*_\[\]`~|])")
            .InDirectory(FormattingSourceDirectory())
            .Matches;

        offenders.ShouldBeEmpty(
            "No message formatter may interpolate a raw value into active MarkdownV2 markup (rule 9): "
            + string.Join("; ", offenders));
    }

    private static string ScrapersSourceDirectory() =>
        Path.Combine(RepositoryRoot(), "src", "JobHunter.Scrapers");

    private static string FormattingSourceDirectory() =>
        Path.Combine(RepositoryRoot(), "src", "JobHunter.Telegram", "Formatting");

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "JobHunter.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate the repository root (JobHunter.slnx).");
        }

        return dir.FullName;
    }
}
