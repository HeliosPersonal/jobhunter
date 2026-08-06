namespace JobHunter.Domain.Preferences;

/// <summary>
/// How an Owner override outranks learning for a <c>(dimension, value)</c> (F7 [[data-model]]
/// §suppression_overrides). These rules are the escape hatch for AC-06/AC-07: they win over whatever the
/// model infers, in either direction.
/// </summary>
public enum SuppressionMode
{
    /// <summary>Guarantee the category keeps appearing regardless of what the model infers (AC-06).</summary>
    NeverSuppress,

    /// <summary>Always suppress the category, whatever the model would otherwise do.</summary>
    AlwaysSuppress,
}
