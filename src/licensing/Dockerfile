# Licensing API image. Same digest-pinned SDK/runtime as the merchant API image;
# refresh them together, in a reviewed commit.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
WORKDIR /app

COPY ["global.json", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]

# Every project in the API's reference closure, licensing and shared, before
# restore, so a shared project edit invalidates this layer for both images.
COPY ["src/licensing/Intelibill.Licensing.Api/Intelibill.Licensing.Api.csproj", "src/licensing/Intelibill.Licensing.Api/"]
COPY ["src/licensing/Intelibill.Licensing.Application/Intelibill.Licensing.Application.csproj", "src/licensing/Intelibill.Licensing.Application/"]
COPY ["src/licensing/Intelibill.Licensing.Domain/Intelibill.Licensing.Domain.csproj", "src/licensing/Intelibill.Licensing.Domain/"]
COPY ["src/licensing/Intelibill.Licensing.Infrastructure/Intelibill.Licensing.Infrastructure.csproj", "src/licensing/Intelibill.Licensing.Infrastructure/"]
COPY ["src/shared/backend/Intelibill.Shared.Web/Intelibill.Shared.Web.csproj", "src/shared/backend/Intelibill.Shared.Web/"]
COPY ["src/shared/backend/Intelibill.Shared.Contracts/Intelibill.Shared.Contracts.csproj", "src/shared/backend/Intelibill.Shared.Contracts/"]
COPY ["src/shared/backend/Intelibill.Shared.Security/Intelibill.Shared.Security.csproj", "src/shared/backend/Intelibill.Shared.Security/"]
RUN dotnet restore "src/licensing/Intelibill.Licensing.Api/Intelibill.Licensing.Api.csproj"

COPY ["src/licensing/", "src/licensing/"]
COPY ["src/shared/backend/", "src/shared/backend/"]

WORKDIR "/app/src/licensing/Intelibill.Licensing.Api"
RUN dotnet publish "Intelibill.Licensing.Api.csproj" --no-restore -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee AS runtime
WORKDIR /app

# libgssapi-krb5-2 is a PostgreSQL connectivity dependency; curl serves the
# container health check. apt/libapt-pkg are upgraded past CVE-2026-109518
# (fixed in 2.8.4ubuntu0.1), which the newest pinned base still ships.
RUN apt-get update \
    && apt-get install -y --no-install-recommends --only-upgrade apt libapt-pkg6.0t64 \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Production unless the deployment says otherwise; the local stack states
# Development explicitly. The API never migrates: schema belongs to the
# licensing migration image (Dockerfile.migrate).
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

USER $APP_UID

ENTRYPOINT ["dotnet", "Intelibill.Licensing.Api.dll"]
