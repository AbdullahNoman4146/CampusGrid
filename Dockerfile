# =======================================================
# Build Stage
# =======================================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["CampusGrid.csproj", "./"]
RUN dotnet restore "CampusGrid.csproj"

# Copy source code and publish
COPY . .
RUN dotnet publish "CampusGrid.csproj" -c Release -o /app/publish /p:UseAppHost=false

# =======================================================
# Runtime Stage
# =======================================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Ensure binding to all interfaces on port 8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Expose HTTP port
EXPOSE 8080

# Run as standard non-root container user
USER app

# Copy published artifacts from build stage
COPY --from=build --chown=app:app /app/publish .

ENTRYPOINT ["dotnet", "CampusGrid.dll"]
