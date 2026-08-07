using JobHunter.Domain.Reporting;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port behind the <c>precision@10</c> comparison (F7 T09 done-when 4, AC-08): a per-Run series of
/// <see cref="PrecisionAtTenPoint"/>, in Run order, each carrying whether it was produced before or after a
/// learned model was active. It is what makes "was any of this worth building?" answerable from recorded data
/// alone — plot the series, split on <see cref="PrecisionAtTenPoint.AfterActivation"/>, and the two halves are
/// directly comparable. Read-only (Dapper, architecture rule 4); defined in Domain so a caller depends on the
/// port, not the SQL.
///
/// <para>It reads the shown (never the suppressed) top-ten of each Run and their positive signals, and
/// <strong>nothing about the Owner's CV</strong> — the CV crosses exactly one boundary, and it is not this one.</para>
/// </summary>
public interface IPrecisionAtTenQuery
{
    /// <summary>The precision@10 of every Run that showed at least one job, oldest Run first.</summary>
    Task<IReadOnlyList<PrecisionAtTenPoint>> SeriesAsync(CancellationToken cancellationToken = default);
}
