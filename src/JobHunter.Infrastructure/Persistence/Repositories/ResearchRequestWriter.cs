using JobHunter.Domain.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// The write side of the on-demand <c>/company</c> command (F8 T09, SAD §6.2, AC-05). Implements
/// <see cref="IResearchRequestWriter"/>: enqueue goes in with <c>ON CONFLICT DO NOTHING</c> against the partial
/// unique index <c>uq_research_requests_open</c> — one open request per company — so asking about the same
/// company twice before the next research cycle drains the queue is an idempotent no-op with no read-then-write
/// race. The id and timestamp come from the injected <see cref="IIdGenerator"/> and <see cref="IClock"/>, so
/// the write is deterministic under test. The row carries only the company id and a short reason —
/// <strong>nothing about the Owner's CV</strong>.
/// </summary>
internal sealed class ResearchRequestWriter(
    INpgsqlConnectionFactory connectionFactory,
    IIdGenerator idGenerator,
    IClock clock) : IResearchRequestWriter
{
    private const string EnqueueSql =
        """
        INSERT INTO research_requests (id, company_id, reason, requested_at, consumed)
        VALUES (@id, @company_id, @reason, @requested_at, false)
        ON CONFLICT (company_id) WHERE NOT consumed DO NOTHING;
        """;

    private readonly INpgsqlConnectionFactory _connectionFactory =
        connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

    private readonly IIdGenerator _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task EnqueueAsync(Guid companyId, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(EnqueueSql, connection);
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = _idGenerator.NewId() });
        command.Parameters.Add(new NpgsqlParameter("company_id", NpgsqlDbType.Uuid) { Value = companyId });
        command.Parameters.Add(new NpgsqlParameter("reason", NpgsqlDbType.Text) { Value = reason.Trim() });
        command.Parameters.Add(new NpgsqlParameter("requested_at", NpgsqlDbType.TimestampTz) { Value = _clock.UtcNow });

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
