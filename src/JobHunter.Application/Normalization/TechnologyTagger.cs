using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Normalization;

/// <summary>
/// Tags a candidate <see cref="Job"/> with the deterministic, vocabulary-matched technologies found in its
/// title and description (T07, SAD §6.1). It runs the curated <see cref="TechnologyVocabulary"/> over the
/// two texts and records each canonical technology once, stamping <see cref="TechnologyMatch.Title"/> when
/// it appears in the title and <see cref="TechnologyMatch.Description"/> when it appears only in the body —
/// so a title hit can be weighted more heavily downstream. It is a <strong>pure function</strong> of the
/// job and the vocabulary: no clock, no randomness, no I/O, no model. F3 later adds inferred technologies to
/// its own enrichment store and never writes <c>job_technologies</c>, so the deterministic set this produces
/// stays separable from the inferred one.
/// </summary>
public sealed class TechnologyTagger(TechnologyVocabulary vocabulary)
{
    private readonly TechnologyVocabulary _vocabulary =
        vocabulary ?? throw new ArgumentNullException(nameof(vocabulary));

    /// <summary>
    /// Adds a technology tag for every vocabulary term occurring as a whole token in
    /// <paramref name="job"/>'s title or description. A title match wins over a description-only match for
    /// the same technology, because <see cref="Job.AddTechnology"/> is idempotent per canonical name and the
    /// title pass runs first. Mutates the job in place and returns it for chaining.
    /// </summary>
    public Job Tag(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);

        foreach (var technology in _vocabulary.Match(job.Title))
        {
            job.AddTechnology(technology, TechnologyMatch.Title);
        }

        foreach (var technology in _vocabulary.Match(job.Description))
        {
            job.AddTechnology(technology, TechnologyMatch.Description);
        }

        return job;
    }
}
