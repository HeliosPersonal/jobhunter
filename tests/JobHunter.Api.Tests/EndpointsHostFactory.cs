using JobHunter.Domain.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace JobHunter.Api.Tests;

/// <summary>
/// Boots the real <see cref="Program"/> with the F9 read ports replaced by NSubstitute fakes, so the
/// search and job endpoints can be exercised end-to-end (routing, auth, the scope-plus-Owner gate, the
/// RFC 7807 problem shapes) with zero network and a controllable read side. Authentication is the same
/// header-driven test scheme <see cref="ApiHostFactory"/> uses.
/// </summary>
public sealed class EndpointsHostFactory : WebApplicationFactory<Program>
{
    /// <summary>The Owner subject the host is configured with; a token for any other subject is a 403.</summary>
    public const string OwnerSubject = "owner-subject-123";

    public ISearchQuery Search { get; } = Substitute.For<ISearchQuery>();

    public IJobRepository Jobs { get; } = Substitute.For<IJobRepository>();

    public ICompanyRepository Companies { get; } = Substitute.For<ICompanyRepository>();

    public ILiveJobsQuery LiveJobs { get; } = Substitute.For<ILiveJobsQuery>();

    public ICompanyJobsQuery CompanyJobs { get; } = Substitute.For<ICompanyJobsQuery>();

    /// <summary>The scheduler the operational endpoints enqueue reindex and reprocess through (T07).</summary>
    public IOperationScheduler Operations { get; } = Substitute.For<IOperationScheduler>();

    /// <summary>The write port behind the source-release service; a substitute keeps the endpoint offline.</summary>
    public IJobSourceRepository Sources { get; } = Substitute.For<IJobSourceRepository>();

    /// <summary>The live-job count the corpus-stats service reads (the authoritative PostgreSQL side).</summary>
    public ILiveJobCounter LiveJobCounter { get; } = Substitute.For<ILiveJobCounter>();

    /// <summary>The search index the corpus-stats service counts against; failures exercise QG-3.</summary>
    public ISearchIndex Index { get; } = Substitute.For<ISearchIndex>();

    /// <summary>The active-Profile lookup behind the CV upload service (T03); a substitute keeps it offline.</summary>
    public IProfileRepository Profiles { get; } = Substitute.For<IProfileRepository>();

    /// <summary>The CV version repository behind the upload service (hash lookup, versioning, activation).</summary>
    public ICvVersionRepository CvVersions { get; } = Substitute.For<ICvVersionRepository>();

    /// <summary>The in-process text extractor behind the upload service; a substitute needs no real bytes.</summary>
    public ICvTextExtractor CvTextExtractor { get; } = Substitute.For<ICvTextExtractor>();

    /// <summary>The match repository the re-match scheduler re-stales through when a new CV version activates.</summary>
    public IMatchRepository Matches { get; } = Substitute.For<IMatchRepository>();

    /// <summary>The re-match backlog the scheduler enqueues recent live jobs onto when a new CV version activates.</summary>
    public IReMatchBacklog ReMatchBacklog { get; } = Substitute.For<IReMatchBacklog>();

    /// <summary>The pipeline read model behind <c>GET /api/applications</c> (F6 T09, AC-01).</summary>
    public IApplicationPipelineQuery ApplicationPipeline { get; } = Substitute.For<IApplicationPipelineQuery>();

    /// <summary>The single-application history read behind <c>GET /api/applications/{id}</c> and the id→job bridge (AC-03).</summary>
    public IApplicationHistoryQuery ApplicationHistory { get; } = Substitute.For<IApplicationHistoryQuery>();

    /// <summary>The due-reminder read behind <c>GET /api/applications/due</c> (T06).</summary>
    public IDueReminderQuery DueReminders { get; } = Substitute.For<IDueReminderQuery>();

    /// <summary>The application write repository behind the status-change and note handlers (job-keyed, QG-1).</summary>
    public IApplicationRepository Applications { get; } = Substitute.For<IApplicationRepository>();

    /// <summary>The job-facts snapshot the outcome-signal publisher stages a weighted signal from (T08).</summary>
    public IJobFactsSnapshotQuery JobFacts { get; } = Substitute.For<IJobFactsSnapshotQuery>();

    /// <summary>The outcome-signal writer the status-change handler stages an F7 signal into (T08).</summary>
    public IOutcomeSignalWriter OutcomeSignals { get; } = Substitute.For<IOutcomeSignalWriter>();

    /// <summary>The preference-model repository behind the weights read, disable and reset endpoints (F7 T08 C6).</summary>
    public IPreferenceModelRepository PreferenceModels { get; } = Substitute.For<IPreferenceModelRepository>();

    /// <summary>The learning master switch behind the toggle-learning endpoint (F7 T08 C6, AC-07).</summary>
    public ILearningSwitch LearningSwitch { get; } = Substitute.For<ILearningSwitch>();

    /// <summary>The hidden-jobs read behind <c>GET /api/preferences/hidden</c> (F7 T08 C6, risk D3).</summary>
    public IHiddenJobsQuery HiddenJobs { get; } = Substitute.For<IHiddenJobsQuery>();

    /// <summary>The research read behind <c>GET /api/companies/{domain}/research</c> (F8 T09 C3).</summary>
    public ICompanyResearchQuery CompanyResearch { get; } = Substitute.For<ICompanyResearchQuery>();

    /// <summary>The on-demand request writer behind <c>POST /api/companies/{domain}/research</c> (F8 T09 C3).</summary>
    public IResearchRequestWriter ResearchRequests { get; } = Substitute.For<IResearchRequestWriter>();

    /// <summary>A client presenting a valid Owner token with the given scope (read by default).</summary>
    public HttpClient OwnerClient(string scope = "jobhunter:read")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, scope);
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, OwnerSubject);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");

        builder.UseSetting("ConnectionStrings:JobHunter", "Host=127.0.0.1;Port=1;Database=jobhunter;Username=test;Password=test");
        builder.UseSetting("ConnectionStrings:Messaging", "amqp://guest:guest@127.0.0.1:5672");
        builder.UseSetting("Typesense:BaseUrl", "http://127.0.0.1:1");
        builder.UseSetting("Typesense:ApiKey", "test-key");
        builder.UseSetting("Typesense:EnvironmentPrefix", "test");
        builder.UseSetting("Keycloak:OwnerSubject", OwnerSubject);

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            // Replace the read ports with controllable fakes so no dependency is dialled at request time.
            services.RemoveAll<ISearchQuery>();
            services.AddSingleton(Search);
            services.RemoveAll<IJobRepository>();
            services.AddScoped(_ => Jobs);
            services.RemoveAll<ICompanyRepository>();
            services.AddScoped(_ => Companies);
            services.RemoveAll<ILiveJobsQuery>();
            services.AddScoped(_ => LiveJobs);
            services.RemoveAll<ICompanyJobsQuery>();
            services.AddScoped(_ => CompanyJobs);

            // Operational-endpoint ports (T07): the Hangfire-backed scheduler and the index/counter/source
            // dependencies behind the admin services, so a reindex or stats read dials nothing.
            services.RemoveAll<IOperationScheduler>();
            services.AddSingleton(Operations);
            services.RemoveAll<IJobSourceRepository>();
            services.AddScoped(_ => Sources);
            services.RemoveAll<ILiveJobCounter>();
            services.AddScoped(_ => LiveJobCounter);
            services.RemoveAll<ISearchIndex>();
            services.AddSingleton(Index);

            // CV upload ports (T03): the profile/CV repositories and the text extractor behind the
            // CvUploadService, so an upload sniffs, versions and activates against controllable fakes with
            // no database or file I/O. The service itself is the real one — its logic is under test.
            services.RemoveAll<IProfileRepository>();
            services.AddScoped(_ => Profiles);
            services.RemoveAll<ICvVersionRepository>();
            services.AddScoped(_ => CvVersions);
            services.RemoveAll<ICvTextExtractor>();
            services.AddSingleton(CvTextExtractor);

            // A genuinely new CV version activates inline through the ReMatchScheduler (AC-08), which re-stales
            // matches and enqueues recent live jobs. Substitute its two write ports so activation stays offline;
            // without these the real EF/Dapper adapters dial the unreachable test database and the upload 500s.
            services.RemoveAll<IMatchRepository>();
            services.AddScoped(_ => Matches);
            services.RemoveAll<IReMatchBacklog>();
            services.AddScoped(_ => ReMatchBacklog);

            // F6 application-tracking ports (T09): the three read models the endpoints project and the write
            // side the status-change and note handlers drive. The ChangeApplicationStatusHandler is the real
            // one — its logic is under test — so its collaborators (the repository, the facts snapshot and the
            // outcome-signal writer behind the OutcomeSignalPublisher) are substituted to keep it offline.
            services.RemoveAll<IApplicationPipelineQuery>();
            services.AddScoped(_ => ApplicationPipeline);
            services.RemoveAll<IApplicationHistoryQuery>();
            services.AddScoped(_ => ApplicationHistory);
            services.RemoveAll<IDueReminderQuery>();
            services.AddScoped(_ => DueReminders);
            services.RemoveAll<IApplicationRepository>();
            services.AddScoped(_ => Applications);
            services.RemoveAll<IJobFactsSnapshotQuery>();
            services.AddScoped(_ => JobFacts);
            services.RemoveAll<IOutcomeSignalWriter>();
            services.AddScoped(_ => OutcomeSignals);

            // F7 preference-learning ports (T08 C6): the model repository behind the weights read and the
            // disable/reset write handlers, the learning switch behind the toggle endpoint, and the hidden-jobs
            // read. The ActiveWeightsQuery and the disable/reset/set handlers are the real ones — their logic is
            // under test — so their collaborators are substituted to keep every request offline.
            services.RemoveAll<IPreferenceModelRepository>();
            services.AddScoped(_ => PreferenceModels);
            services.RemoveAll<ILearningSwitch>();
            services.AddScoped(_ => LearningSwitch);
            services.RemoveAll<IHiddenJobsQuery>();
            services.AddScoped(_ => HiddenJobs);

            // F8 research ports (T09 C3): the dossier read behind GET .../research and the on-demand request
            // writer behind POST .../research, so an owner-scoped research read or queue dials nothing.
            services.RemoveAll<ICompanyResearchQuery>();
            services.AddScoped(_ => CompanyResearch);
            services.RemoveAll<IResearchRequestWriter>();
            services.AddScoped(_ => ResearchRequests);
        });
    }
}
