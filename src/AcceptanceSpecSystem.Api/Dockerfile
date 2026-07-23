# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:63ebabdcde24cc8134304a6f50719cf1e22bb4e9f7148ac4ef967c7680187356 AS build
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

FROM mcr.microsoft.com/dotnet/aspnet:8.0@sha256:e78fda31142e28746a6908e288e0d40346793f691ca99d8d150bcbe95c0ef035 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ARG DEBIAN_MIRROR=""

RUN if [ -n "$DEBIAN_MIRROR" ]; then \
      sed -i "s|deb.debian.org|$DEBIAN_MIRROR|g" /etc/apt/sources.list.d/debian.sources; \
    fi \
    && apt-get update \
    && for i in 1 2 3 4 5; do \
         if apt-get install -y --no-install-recommends default-mysql-client gzip curl; then break; fi; \
         if [ "$i" -eq 5 ]; then echo "Package installation failed after $i attempts" >&2; exit 1; fi; \
         echo "Retry $i..."; sleep 10; \
       done \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data/files /data/dp-keys /app/backups

COPY --from=build /app/publish .
COPY --from=build /src/shared/ /app/shared/

# .NET 8 官方镜像提供非 root app 用户（APP_UID）。新建命名卷会继承这些目录的所有权；
# 已有生产卷升级前按部署文档执行一次性权限迁移。
RUN chown -R "$APP_UID":0 /app /data \
    && chmod -R g=u /app /data

USER $APP_UID

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://localhost:8080/health/ready || exit 1

ENTRYPOINT ["dotnet", "AcceptanceSpecSystem.Api.dll"]
