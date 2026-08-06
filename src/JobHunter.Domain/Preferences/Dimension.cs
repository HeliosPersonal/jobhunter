namespace JobHunter.Domain.Preferences;

/// <summary>
/// The closed set of job characteristics a learned preference can be about (F7 SAD §8,
/// [[data-model]] §preference_weights). A <see cref="PreferenceWeight"/> is keyed by a dimension and a
/// value within it — <c>Country=DE</c>, <c>SalaryBand=150-180k</c>, <c>Technology=Kafka</c> — and a
/// <see cref="Signal"/>'s <see cref="JobFacts"/> snapshot is expressed in the same terms, so the fitter
/// aggregates reaction rates per <c>(dimension, value)</c> without a join.
///
/// <para>Persisted as <c>text</c>, never an ordinal (coding-standards §5). This is a <em>closed</em> enum:
/// TUNE-08 / F7 T10 adds <c>AiUsage</c> and <c>RoleFamily</c> as a deliberate, reviewed extension, not an
/// open door — a dimension the learner does not understand cannot earn a weight.</para>
/// </summary>
public enum Dimension
{
    /// <summary>The salary band the posting advertised, normalised (e.g. <c>150-180k</c>).</summary>
    SalaryBand,

    /// <summary>The country the role is in (e.g. <c>DE</c>).</summary>
    Country,

    /// <summary>The hiring company's size band (e.g. <c>SeriesB</c>).</summary>
    CompanySize,

    /// <summary>A technology the role centres on (e.g. <c>Kafka</c>); a job carries several.</summary>
    Technology,

    /// <summary>The timezone band the role expects the Owner to work in.</summary>
    TimezoneBand,

    /// <summary>The remote policy — remote, hybrid or on-site.</summary>
    RemotePolicy,

    /// <summary>The employment type — full-time, contract and so on.</summary>
    EmploymentType,

    /// <summary>How much the role is about building with or on AI systems, from the F3 enrichment
    /// (<c>ai_usage</c>, e.g. <c>High</c>); lets the loop reinforce the Owner's AI trajectory (TUNE-08).</summary>
    AiUsage,

    /// <summary>What the role actually is, classified from the described work by the F3 enrichment
    /// (<c>role_family</c>, e.g. <c>AiPlatform</c>); lets the loop pull toward the target family (TUNE-03/08).</summary>
    RoleFamily,
}
