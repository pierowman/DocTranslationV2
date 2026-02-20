# Windows Container Dockerfile - PRIMARY DEPLOYMENT TARGET
# 
# This provides FULL EMF/WMF/Visio diagram support via native Windows GDI+
# For cost-optimized Linux deployment, see Dockerfile.linux (white placeholder fallback)
#
# Requirements:
# - Windows Server 2019+ or Windows 10/11 Pro with Hyper-V
# - Docker Desktop switched to Windows containers
# - Azure App Service (Windows) or Windows container hosts
#
# Note: Uses Windows Server Core (not nanoserver) because GDI+ is required for EMF/WMF conversion

# Escape character for Windows paths
# escape=`

# Base stage - Windows Server Core with .NET 9 runtime (includes GDI+)
FROM mcr.microsoft.com/dotnet/aspnet:9.0-windowsservercore-ltsc2022 AS base
WORKDIR /app
EXPOSE 8080

# Build stage - SDK image for compilation
FROM mcr.microsoft.com/dotnet/sdk:9.0-windowsservercore-ltsc2022 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project file and restore dependencies
COPY ["DocTranslationV2/DocTranslationV2.csproj", "DocTranslationV2/"]
RUN dotnet restore "DocTranslationV2/DocTranslationV2.csproj"

# Copy source code and build
COPY . .
WORKDIR "/src/DocTranslationV2"
RUN dotnet build "DocTranslationV2.csproj" -c %BUILD_CONFIGURATION% -o /app/build

# Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "DocTranslationV2.csproj" -c %BUILD_CONFIGURATION% -o /app/publish /p:UseAppHost=false

# Final stage - Runtime with published app
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Health check using PowerShell (single line to avoid parsing issues)
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 CMD powershell -Command "try { $response = Invoke-WebRequest -Uri http://localhost:8080/health -UseBasicParsing -ErrorAction Stop; if ($response.StatusCode -eq 200) { exit 0 } else { exit 1 } } catch { exit 1 }"

ENTRYPOINT ["dotnet", "DocTranslationV2.dll"]
