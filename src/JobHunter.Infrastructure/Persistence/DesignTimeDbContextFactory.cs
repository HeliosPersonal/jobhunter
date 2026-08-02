using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobHunter.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> / <c>database update</c> build the context without starting a
/// host or reaching live infrastructure (T05). The connection string is taken from
/// <c>JOBHUNTER_DESIGNTIME_CONNECTION</c> when present, otherwise a localhost default that is only
/// ever used to construct the model — design-time commands like <c>migrations add</c> never open it.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<JobHunterDbContext>
{
    public JobHunterDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("JOBHUNTER_DESIGNTIME_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=jobhunter;Username=postgres;Password=postgres";

        return JobHunterDbContextFactory.Create(connectionString);
    }
}
