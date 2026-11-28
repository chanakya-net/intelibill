# Licensing API image. Same digest-pinned SDK/runtime as the merchant API image;
# refresh them together, in a reviewed commit.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS build
WORKDIR /app

COPY ["global.json", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]

# Every project in the API's reference closure, licensing and shared, before
# restore, so a shared project edit invalidates this layer for both images.
COPY ["src/backend/licensing/Intelibill.Licensing.Api/Intelibill.Licensing.Api.csproj", "src/backend/licensing/Intelibill.Licensing.Api/"]
COPY ["src/backend/licensing/Intelibill.Licensing.Application/Intelibill.Licensing.Application.csproj", "src/backend/licensing/Intelibill.Licensing.Application/"]
COPY ["src/backend/licensing/Intelibill.Licensing.Domain/Intelibill.Licensing.Domain.csproj", "src/backend/licensing/Intelibill.Licensing.Domain/"]
COPY ["src/backend/licensing/Intelibill.Licensing.Infrastructure/Intelibill.Licensing.Infrastructure.csproj", "src/backend/licensing/Intelibill.Licensing.Infrastructure/"]
COPY ["src/backend/shared/Intelibill.Shared.Web/Intelibill.Shared.Web.csproj", "src/backend/shared/Intelibill.Shared.Web/"]
COPY ["src/backend/shared/Intelibill.Shared.Contracts/Intelibill.Shared.Contracts.csproj", "src/backend/shared/Intelibill.Shared.Contracts/"]
COPY ["src/backend/shared/Intelibill.Shared.Security/Intelibill.Shared.Security.csproj", "src/backend/shared/Intelibill.Shared.Security/"]
RUN dotnet restore "src/backend/licensing/Intelibill.Licensing.Api/Intelibill.Licensing.Api.csproj"

COPY ["src/backend/licensing/", "src/backend/licensing/"]
COPY ["src/backend/shared/", "src/backend/shared/"]

WORKDIR "/app/src/backend/licensing/Intelibill.Licensing.Api"
RUN dotnet publish "Intelibill.Licensing.Api.csproj" --no-restore -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:bfd912c83295ce66d4b706ebb8541b9f14259b0e76fbb88b2751f58ce2399d2e AS runtime
WORKDIR /app

# libgssapi-krb5-2 is a PostgreSQL connectivity dependency; curl serves the
# container health check.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Production unless the deployment says otherwise; the local stack states
# Development explicitly. The API never migrates: schema belongs to the
# licensing migration image (Dockerfile.migrate).
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

RUN install -d -m 0700 -o "$APP_UID" /var/lib/intelibill/licensing-keys

USER $APP_UID

ENTRYPOINT ["dotnet", "Intelibill.Licensing.Api.dll"]
