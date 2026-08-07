using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Preferences;
using JobHunter.Domain.Profiles;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Preferences;

/// <summary>
/// The real <see cref="IPreferenceModelQuery"/> that F7 supplies to F4 ranking once learning has landed
/// (SAD §6.2, T06), replacing the <c>NullPreferenceModelQuery</c> F4 shipped with. It loads the one active
/// <see cref="PreferenceModel"/> and its weights, each ranked job's current <see cref="JobFacts"/>, and the
/// active <see cref="Profile"/>'s explicit preferences, then runs the pure
/// <see cref="PreferenceComponentCalculator"/> per job to produce the preference component F4 folds into its
/// score. It stamps the active model's id on the result so a bad refit is attributable after the fact (AC-04).
///
/// <para>Only jobs the model has an opinion on are mapped: a job with no matching (non-disabled, un-overridden)
/// weight is omitted, so F4 renormalises the preference weight away rather than scoring it at a neutral 0.5.
/// The Profile's explicit stances — preferred countries and accepted employment types — override any
/// contradicting learned weight on the same value (AC-05); the conflict is recorded on the component. Facts are
/// read fresh per job (never joined at fit time) and a job whose facts are gone or closed is simply omitted.</para>
///
/// <para>Deliberately in Application, not Infrastructure: it is composition over existing ports (the model and
/// profile repositories, the facts snapshot query) plus a pure calculator, with no SQL of its own. Registered
/// scoped because it holds those scoped collaborators.</para>
/// </summary>
public sealed class PreferenceModelQuery(
    IPreferenceModelRepository models,
    IProfileRepository profiles,
    IJobFactsSnapshotQuery facts,
    ILearningSwitch learning,
    ILogger<PreferenceModelQuery> logger) : IPreferenceModelQuery
{
    private readonly IPreferenceModelRepository _models = models ?? throw new ArgumentNullException(nameof(models));
    private readonly IProfileRepository _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
    private readonly IJobFactsSnapshotQuery _facts = facts ?? throw new ArgumentNullException(nameof(facts));
    private readonly ILearningSwitch _learning = learning ?? throw new ArgumentNullException(nameof(learning));
    private readonly ILogger<PreferenceModelQuery> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<ActivePreference?> FindActiveAsync(
        IReadOnlyCollection<Guid> jobIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobIds);

        if (!await _learning.IsEnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            // Learning is switched off entirely (AC-07): do not even load the model. Ranking renormalises the
            // preference weight away and orders on match, freshness and explicit Profile preferences alone,
            // exactly as if no model had been fitted. The signals survive for when it is turned back on.
            return null;
        }

        var model = await _models.FindActiveAsync(cancellationToken).ConfigureAwait(false);
        if (model is null)
        {
            // No active model yet: ranking renormalises the preference weight away and scores on the rest.
            return null;
        }

        var weights = model.Weights;
        var profile = await _profiles.FindActiveAsync(cancellationToken).ConfigureAwait(false);
        var stances = ExplicitStancesOf(profile);

        var componentByJob = new Dictionary<Guid, decimal>();
        foreach (var jobId in jobIds)
        {
            var jobFacts = await _facts.SnapshotAsync(jobId, cancellationToken).ConfigureAwait(false);
            if (jobFacts is null)
            {
                // The job is gone, closed or superseded: it has no facts to score and is omitted, never
                // scored on a stale join to a job that may have been edited since (T03).
                continue;
            }

            var component = PreferenceComponentCalculator.Calculate(weights, jobFacts, stances);
            if (component is not null)
            {
                componentByJob[jobId] = component.Value;
            }
        }

        _logger.LogInformation(
            "Applied preference model {Version} to {Total} ranked job(s); {Scored} carried a learned opinion.",
            model.Version, jobIds.Count, componentByJob.Count);

        return new ActivePreference(model.Id, componentByJob);
    }

    /// <summary>
    /// Projects the active Profile's <em>explicit</em> preferences into the learner's <c>(dimension, value)</c>
    /// vocabulary so they can override contradicting learned weights (AC-05): the preferred countries are
    /// positive <see cref="Dimension.Country"/> stances, the accepted employment types positive
    /// <see cref="Dimension.EmploymentType"/> stances. No Profile means no explicit stances — the learned
    /// weights stand on their own.
    /// </summary>
    private static List<ExplicitStance> ExplicitStancesOf(Profile? profile)
    {
        if (profile is null)
        {
            return [];
        }

        var stances = new List<ExplicitStance>();
        foreach (var country in profile.PreferredCountries)
        {
            stances.Add(new ExplicitStance(Dimension.Country, country, IsPositive: true));
        }

        foreach (var employmentType in profile.EmploymentTypes)
        {
            stances.Add(new ExplicitStance(Dimension.EmploymentType, employmentType.ToString(), IsPositive: true));
        }

        return stances;
    }
}
