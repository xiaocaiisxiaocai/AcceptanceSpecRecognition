FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj for restore caching
COPY AcceptanceSpecSystem.sln ./
COPY src/AcceptanceSpecSystem.Api/AcceptanceSpecSystem.Api.csproj src/AcceptanceSpecSystem.Api/
COPY src/AcceptanceSpecSystem.Core/AcceptanceSpecSystem.Core.csproj src/AcceptanceSpecSystem.Core/
COPY src/AcceptanceSpecSystem.Data/AcceptanceSpecSystem.Data.csproj src/AcceptanceSpecSystem.Data/

RUN dotnet restore src/AcceptanceSpecSystem.Api/AcceptanceSpecSystem.Api.csproj

# Copy source
COPY src ./src

RUN dotnet publish src/AcceptanceSpecSystem.Api/AcceptanceSpecSystem.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# default: listen on 8080 inside container
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

# uploads folder (bind-mount in docker-compose)
RUN mkdir -p /app/uploads

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "AcceptanceSpecSystem.Api.dll"]

