FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/AdGuardHomeElasticLogs.csproj", "./"]
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore "AdGuardHomeElasticLogs.csproj"
COPY . .
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet build "AdGuardHomeElasticLogs.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish "AdGuardHomeElasticLogs.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AdGuardHomeElasticLogs.dll"]
