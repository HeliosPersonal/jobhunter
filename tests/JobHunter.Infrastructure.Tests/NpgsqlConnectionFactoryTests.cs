using JobHunter.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests;

public sealed class NpgsqlConnectionFactoryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_connection_string_is_rejected_at_construction(string? connectionString)
    {
        Should.Throw<ArgumentException>(() => new NpgsqlConnectionFactory(connectionString!));
    }

    [Fact]
    public void A_valid_connection_string_constructs()
    {
        // Construction must not open a connection — that happens lazily in OpenAsync.
        Should.NotThrow(() => new NpgsqlConnectionFactory("Host=localhost;Database=jh;Username=u;Password=p"));
    }
}
