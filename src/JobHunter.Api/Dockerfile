# Multi-stage build for JobHunter.Api. Built from the repository root (context: .) so Central Package
# Management and the shared props participate in restore. See docs/engineering/deployment.md §7.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the solution-level build inputs first so restore is cached across source-only changes.
COPY Directory.Build.props Directory.Packages.props nuget.config ./

# Copy every referenced project file before the source, for the same layer-cache reason.
COPY src/JobHunter.Api/*.csproj             src/JobHunter.Api/
COPY src/JobHunter.Application/*.csproj     src/JobHunter.Application/
COPY src/JobHunter.Domain/*.csproj          src/JobHunter.Domain/
COPY src/JobHunter.Infrastructure/*.csproj  src/JobHunter.Infrastructure/
COPY src/JobHunter.Contracts/*.csproj       src/JobHunter.Contracts/
COPY src/JobHunter.Claude/*.csproj          src/JobHunter.Claude/
COPY src/JobHunter.Scrapers/*.csproj        src/JobHunter.Scrapers/
COPY src/JobHunter.Search/*.csproj          src/JobHunter.Search/
COPY src/Aspire/JobHunter.ServiceDefaults/*.csproj src/Aspire/JobHunter.ServiceDefaults/
RUN dotnet restore src/JobHunter.Api/JobHunter.Api.csproj

COPY . .
RUN dotnet publish src/JobHunter.Api/JobHunter.Api.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# Explicit non-root user (invariant 12 / deployment §7); not relying on the base image default.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "JobHunter.Api.dll"]
