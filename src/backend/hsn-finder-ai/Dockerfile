# HSN Finder API image. Same digest-pinned SDK/runtime as the merchant and
# licensing API images; refresh them together, in a reviewed commit. Build with
# the repository root as the context. The service has its own
# Directory.Build.props / Directory.Packages.props, so the root ones are not
# copied. appsettings.secrets.json is excluded by .dockerignore and must stay
# out of the image: it would override environment variables at startup.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS build
WORKDIR /app

COPY ["global.json", "./"]
COPY ["src/backend/hsn-finder-ai/Directory.Build.props", "src/backend/hsn-finder-ai/"]
COPY ["src/backend/hsn-finder-ai/Directory.Packages.props", "src/backend/hsn-finder-ai/"]
COPY ["src/backend/hsn-finder-ai/Sankra.HsnFinder.Api/HsnFinder.Api.csproj", "src/backend/hsn-finder-ai/Sankra.HsnFinder.Api/"]
RUN dotnet restore "src/backend/hsn-finder-ai/Sankra.HsnFinder.Api/HsnFinder.Api.csproj"

COPY ["src/backend/hsn-finder-ai/", "src/backend/hsn-finder-ai/"]

WORKDIR "/app/src/backend/hsn-finder-ai/Sankra.HsnFinder.Api"
RUN dotnet publish "HsnFinder.Api.csproj" --no-restore -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:bfd912c83295ce66d4b706ebb8541b9f14259b0e76fbb88b2751f58ce2399d2e AS runtime
WORKDIR /app

# curl serves the container health check. The service has no database, so no
# PostgreSQL client library is needed.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Production unless the deployment says otherwise; the local stack states
# Development explicitly. Provider key and model come from the environment.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

USER $APP_UID

ENTRYPOINT ["dotnet", "HsnFinder.Api.dll"]
