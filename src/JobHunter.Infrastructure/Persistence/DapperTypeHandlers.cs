using System.Data;
using Dapper;

namespace JobHunter.Infrastructure.Persistence;

/// <summary>
/// Global Dapper type handlers, registered exactly once (idempotently) before the first Dapper read.
/// Every read model materialises the same way — whether the query is resolved through DI or
/// constructed directly in a test — because the registration is triggered from the one chokepoint all
/// reads pass through, <see cref="NpgsqlConnectionFactory"/>.
///
/// Npgsql surfaces a <c>timestamptz</c> column as <see cref="DateTime"/> (UTC kind). Read DTOs use
/// <see cref="DateTimeOffset"/> to match the domain's UTC-everywhere convention (coding-standards),
/// and Dapper cannot bridge that gap on its own — a positional record whose parameter is
/// <c>DateTimeOffset</c> finds no constructor matching a <c>DateTime</c> column. Registering this
/// handler is what makes the canonical read-model pattern (a flat <c>record</c> with hand-written SQL)
/// work for every feature. This is the one place it is wired; feature read models just declare
/// <c>DateTimeOffset</c> properties and copy the query shape.
/// </summary>
internal static class DapperTypeHandlers
{
    private static int _registered;

    /// <summary>
    /// Registers the JobHunter Dapper type handlers once. Safe to call repeatedly and concurrently;
    /// only the first call has an effect.
    /// </summary>
    internal static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
        {
            SqlMapper.AddTypeHandler(new UtcDateTimeOffsetHandler());
        }
    }

    /// <summary>
    /// Reads <c>timestamptz</c> (a UTC <see cref="DateTime"/> from Npgsql) into a
    /// <see cref="DateTimeOffset"/> with a zero offset, and writes a <see cref="DateTimeOffset"/>
    /// query parameter back as its UTC instant.
    /// </summary>
    private sealed class UtcDateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override DateTimeOffset Parse(object value) => value switch
        {
            DateTimeOffset dto => dto.ToUniversalTime(),
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => throw new DataException(
                $"Cannot convert {value?.GetType().Name ?? "null"} to DateTimeOffset."),
        };

        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        {
            parameter.Value = value.UtcDateTime;
        }
    }
}
