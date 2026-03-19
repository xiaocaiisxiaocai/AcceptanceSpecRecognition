FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY AcceptanceSpecSystem.sln ./
COPY src/AcceptanceSpecSystem.Api/AcceptanceSpecSystem.Api.csproj src/AcceptanceSpecSystem.Api/
COPY src/AcceptanceSpecSystem.Core/AcceptanceSpecSystem.Core.csproj src/AcceptanceSpecSystem.Core/
COPY src/AcceptanceSpecSystem.Data/AcceptanceSpecSystem.Data.csproj src/AcceptanceSpecSystem.Data/

RUN dotnet restore src/AcceptanceSpecSystem.Api/AcceptanceSpecSystem.Api.csproj

COPY . .
RUN dotnet publish src/AcceptanceSpecSystem.Api/AcceptanceSpecSystem.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080

RUN mkdir -p /data/files /data/dp-keys

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "AcceptanceSpecSystem.Api.dll"]
