namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port behind the digest's "N companies checked, nothing matched" reassurance line (AC-05, F5
/// message contract §NothingNew). Returns how many active companies the pipeline is currently scanning, so a
/// day that found nothing can state the scope it looked across rather than an unqualified "nothing" — the
/// difference between "the system is idle" and "the system looked hard and there was genuinely nothing".
/// Read-only (Dapper); defined in Domain so the assembler depends on the port, not the SQL. It selects
/// <strong>nothing about the Owner</strong> — the CV crosses exactly one boundary, and it is not this one.
/// </summary>
public interface IActiveCompanyCountQuery
{
    /// <summary>The number of active companies in the registry — the scope a "nothing matched" day covered.</summary>
    Task<int> ActiveCompanyCountAsync(CancellationToken cancellationToken = default);
}
