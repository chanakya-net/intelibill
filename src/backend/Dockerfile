# Base images are pinned by digest rather than the floating 10.0 tag. Two builds
# of the same commit must produce the same image, and a rollback to a recorded
# digest is only a rollback if the base cannot move underneath it. Refresh these
# deliberately, in a reviewed commit.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:e1ffd2a92ae84c1291bc1b6887501f8af98e6331e7af6d4c8d37168c5e87a64c AS build
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
RUN dotnet run --no-launch-profile --no-restore -c Release -- codegen write \
    && dotnet publish "Intelibill.Api.csproj" --no-restore -c Release -o /app/publish /p:UseAppHost=false

# Use the ASP.NET 10.0 runtime for the final image
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:a4556ed033fa96f984bb7a8d348851cb2d36b1281dd2420070045f664fbb5f94 AS runtime
WORKDIR /app

# Installed before the user switch, which needs root. libgssapi-krb5-2 is a
# PostgreSQL connectivity dependency; curl serves the container health check.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

# APP_UID is the non-root account the .NET base image already ships (1654).
# Nothing here writes to the filesystem and the schema belongs to the migration
# job, so the runtime never needs a privilege it cannot get back.
USER $APP_UID

# Run the app
ENTRYPOINT ["dotnet", "Intelibill.Api.dll"]
