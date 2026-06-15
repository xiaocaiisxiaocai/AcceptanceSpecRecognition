# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY AcceptanceSpecSystem.sln ./
COPY Directory.Build.props ./
COPY src/AcceptanceSpecSystem.Api/AcceptanceSpecSystem.Api.csproj src/AcceptanceSpecSystem.Api/
COPY src/AcceptanceSpecSystem.Application/AcceptanceSpecSystem.Application.csproj src/AcceptanceSpecSystem.Application/
COPY src/AcceptanceSpecSystem.Core/AcceptanceSpecSystem.Core.csproj src/AcceptanceSpecSystem.Core/
COPY src/AcceptanceSpecSystem.Data/AcceptanceSpecSystem.Data.csproj src/AcceptanceSpecSystem.Data/
COPY shared/ shared/

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore src/AcceptanceSpecSystem.Api/AcceptanceSpecSystem.Api.csproj

COPY src/ src/
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/AcceptanceSpecSystem.Api/AcceptanceSpecSystem.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080

RUN sed -i 's/deb.debian.org/mirrors.tencent.com/g' /etc/apt/sources.list.d/debian.sources \
    && apt-get update \
    && for i in 1 2 3 4 5; do \
         apt-get install -y --no-install-recommends default-mysql-client gzip curl && break \
         || echo "Retry $i..." && sleep 10; \
       done \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data/files /data/dp-keys /app/backups

COPY --from=build /app/publish .
COPY --from=build /src/shared/ /app/shared/

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "AcceptanceSpecSystem.Api.dll"]
