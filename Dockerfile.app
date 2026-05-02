# Root Dockerfile for App - used by Render
# This file redirects to the actual Dockerfile in DRC.App/

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
# Constrain memory usage so Render free-tier (512MB) doesn't OOM-kill the build
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_GCServer=0 \
    DOTNET_GCConcurrent=0 \
    DOTNET_TieredCompilation=0
WORKDIR /src

# Copy only csproj files first for better layer caching
COPY DRC.App/DRC.App.csproj DRC.App/
COPY DRC.Api.ServiceDefaults/DRC.Api.ServiceDefaults.csproj DRC.Api.ServiceDefaults/

# Restore (single-threaded to keep RAM low)
RUN dotnet restore DRC.App/DRC.App.csproj /p:DisableImplicitNuGetFallbackFolder=true -m:1

# Copy only the necessary source files
COPY DRC.App/ DRC.App/
COPY DRC.Api.ServiceDefaults/ DRC.Api.ServiceDefaults/

# Build and publish, single-threaded, no Razor parallel compile (avoids OOM on 512MB)
WORKDIR /src/DRC.App
RUN dotnet publish -c $BUILD_CONFIGURATION -o /app/publish \
    --no-restore \
    /p:UseAppHost=false \
    /p:UseRazorBuildServer=false \
    /p:UseSharedCompilation=false \
    /p:PublishReadyToRun=false \
    -m:1

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DRC.App.dll"]
