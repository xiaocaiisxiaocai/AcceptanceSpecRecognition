FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY AcceptanceSpecSystem.sln ./
COPY Directory.Build.props ./
COPY src/AcceptanceSpecSystem.Api/AcceptanceSpecSystem.Api.csproj src/AcceptanceSpecSystem.Api/
COPY src/AcceptanceSpecSystem.Application/AcceptanceSpecSystem.Application.csproj src/AcceptanceSpecSystem.Application/
COPY src/AcceptanceSpecSystem.Core/AcceptanceSpecSystem.Core.csproj src/AcceptanceSpecSystem.Core/
COPY src/AcceptanceSpecSystem.Data/AcceptanceSpecSystem.Data.csproj src/AcceptanceSpecSystem.Data/

RUN dotnet restore src/AcceptanceSpecSystem.Api/AcceptanceSpecSystem.Api.csproj

COPY . .
RUN dotnet publish src/AcceptanceSpecSystem.Api/AcceptanceSpecSystem.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080

RUN apt-get update \
    && apt-get install -y --no-install-recommends default-mysql-client gzip curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data/files /data/dp-keys /app/backups

COPY --from=build /app/publish .
COPY shared/ /app/shared/

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "AcceptanceSpecSystem.Api.dll"]
