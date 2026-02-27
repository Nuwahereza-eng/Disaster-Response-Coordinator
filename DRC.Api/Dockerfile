#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy only csproj files first for better layer caching
COPY DRC.Api/DRC.Api.csproj DRC.Api/
COPY DRC.Api.ServiceDefaults/DRC.Api.ServiceDefaults.csproj DRC.Api.ServiceDefaults/

# Restore as a separate layer
RUN dotnet restore DRC.Api/DRC.Api.csproj

# Copy only the necessary source files
COPY DRC.Api/ DRC.Api/
COPY DRC.Api.ServiceDefaults/ DRC.Api.ServiceDefaults/

# Build and publish in one step
WORKDIR /src/DRC.Api
RUN dotnet publish -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DRC.Api.dll"]