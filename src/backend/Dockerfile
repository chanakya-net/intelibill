# Use the .NET 10.0 SDK for building
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy the root-level property files for NuGet package management
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]

# Copy project files and restore dependencies
COPY ["src/backend/Intelibill.Api/Intelibill.Api.csproj", "src/backend/Intelibill.Api/"]
COPY ["src/backend/Intelibill.Application/Intelibill.Application.csproj", "src/backend/Intelibill.Application/"]
COPY ["src/backend/Intelibill.Domain/Intelibill.Domain.csproj", "src/backend/Intelibill.Domain/"]
COPY ["src/backend/Intelibill.Infrastructure/Intelibill.Infrastructure.csproj", "src/backend/Intelibill.Infrastructure/"]

# Restore NuGet packages
RUN dotnet restore "src/backend/Intelibill.Api/Intelibill.Api.csproj"

# Copy the rest of the backend source code
COPY ["src/backend/", "src/backend/"]

# Build and publish the API project
WORKDIR "/app/src/backend/Intelibill.Api"
RUN dotnet publish "Intelibill.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Use the ASP.NET 10.0 runtime for the final image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install libgssapi-krb5-2 for PostgreSQL connectivity and curl for health checking
RUN apt-get update && apt-get install -y libgssapi-krb5-2 curl && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Expose ports
EXPOSE 8080
EXPOSE 8081

# Set the environment to Production
ENV ASPNETCORE_ENVIRONMENT=Production

# Run the app
ENTRYPOINT ["dotnet", "Intelibill.Api.dll"]
