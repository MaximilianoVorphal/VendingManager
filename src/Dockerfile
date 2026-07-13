# Base stage for runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy csproj files for Restore
COPY ["VendingManager/VendingManager.csproj", "VendingManager/"]
COPY ["VendingManager.Web/VendingManager.Web.csproj", "VendingManager.Web/"]
COPY ["VendingManager.Shared/VendingManager.Shared.csproj", "VendingManager.Shared/"]

# Restore dependencies (cache mount evita redescargar NuGet si la cache local existe)
RUN --mount=type=cache,id=nuget,sharing=locked,target=/root/.nuget/packages \
    dotnet restore "VendingManager/VendingManager.csproj"

# Copy the rest of the source code
COPY . .
WORKDIR "/src/VendingManager"

# Build the project
RUN --mount=type=cache,id=nuget,sharing=locked,target=/root/.nuget/packages \
    dotnet build "VendingManager.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN --mount=type=cache,id=nuget,sharing=locked,target=/root/.nuget/packages \
    dotnet publish "VendingManager.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "VendingManager.dll"]
