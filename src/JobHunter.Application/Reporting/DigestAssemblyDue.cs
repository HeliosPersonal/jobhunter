namespace JobHunter.Application.Reporting;

/// <summary>
/// The 06:45 Europe/Kyiv tick that assembles the day's digest against whatever state the Run is in
/// (ADR-F5-0001, SAD §6.3). Enqueued by Hangfire and handled by <see cref="DigestAssembler"/>: it resolves
/// the day's Run and assembles the digest — the normal one when ranking finished, a degraded variant
/// otherwise — so every morning produces a digest, never silence.
///
/// <para>Assembly is idempotent on <c>uq_digests_run</c>, so this tick is <em>assemble-if-absent</em>: if
/// the happy path already assembled earlier off <c>RankingCompleted</c>, this is a no-op re-emit; otherwise
/// it assembles the variant the Run's 06:45 state earned. An internal application message, not a
/// cross-boundary integration event, so it lives in the Application layer rather than <c>Contracts</c>.
/// <see cref="DueAt"/> is stamped once when the tick fires.</para>
/// </summary>
public sealed record DigestAssemblyDue(DateTimeOffset DueAt);
