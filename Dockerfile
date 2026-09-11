# ClaimPilot API image
# Multi-stage: SDK build -> self-contained publish -> slim aspnet runtime.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ClaimPilot.slnx ./
COPY src/ src/
COPY tests/ tests/

WORKDIR /src/src/ClaimPilot.API
RUN dotnet restore ClaimPilot.API.csproj
RUN dotnet publish ClaimPilot.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ClaimPilot.API.dll"]