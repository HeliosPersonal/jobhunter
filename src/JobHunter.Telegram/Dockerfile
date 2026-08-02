# Multi-stage build for JobHunter.Telegram. Built from the repository root (context: .).
# See docs/engineering/deployment.md §7.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props nuget.config ./

COPY src/JobHunter.Telegram/*.csproj        src/JobHunter.Telegram/
COPY src/JobHunter.Application/*.csproj     src/JobHunter.Application/
COPY src/JobHunter.Domain/*.csproj          src/JobHunter.Domain/
COPY src/JobHunter.Infrastructure/*.csproj  src/JobHunter.Infrastructure/
COPY src/JobHunter.Contracts/*.csproj       src/JobHunter.Contracts/
COPY src/JobHunter.Claude/*.csproj          src/JobHunter.Claude/
COPY src/JobHunter.Scrapers/*.csproj        src/JobHunter.Scrapers/
COPY src/JobHunter.Search/*.csproj          src/JobHunter.Search/
COPY src/Aspire/JobHunter.ServiceDefaults/*.csproj src/Aspire/JobHunter.ServiceDefaults/
RUN dotnet restore src/JobHunter.Telegram/JobHunter.Telegram.csproj

COPY . .
RUN dotnet publish src/JobHunter.Telegram/JobHunter.Telegram.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "JobHunter.Telegram.dll"]
