# Root Dockerfile for App - used by Render
# This file redirects to the actual Dockerfile in DRC.App/

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy only csproj files first for better layer caching
COPY DRC.App/DRC.App.csproj DRC.App/
COPY DRC.Api.ServiceDefaults/DRC.Api.ServiceDefaults.csproj DRC.Api.ServiceDefaults/

# Restore as a separate layer
RUN dotnet restore DRC.App/DRC.App.csproj

# Copy only the necessary source files
COPY DRC.App/ DRC.App/
COPY DRC.Api.ServiceDefaults/ DRC.Api.ServiceDefaults/

# Build and publish in one step
WORKDIR /src/DRC.App
RUN dotnet publish -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DRC.App.dll"]
