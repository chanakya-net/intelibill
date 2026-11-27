# Barcode Master API

Small layered ASP.NET Core API that stores product records keyed by barcode and looks them up. A single-barcode lookup that misses the local PostgreSQL database falls back to Open Food Facts and saves the hit.

It is a separately deployable service inside the [intelibill monorepo](../../../README.md) with its own solution; it is not part of `Intelibill.slnx` and shares no code with the other backend projects.

## Stack

- .NET 10 (`net10.0`), ASP.NET Core MVC controllers; SDK pinned by the root `global.json`
- PostgreSQL via EF Core 10 + Npgsql (writes, migrations) and Dapper (read and batch SQL)
- JWT Bearer and API key authentication behind one `DualAuth` policy
- Typed `HttpClient` for Open Food Facts
- Wolverine host integration (`UseWolverine()`), `Microsoft.AspNetCore.OpenApi`
- xUnit for tests

Package versions are pinned in each `.csproj`. The local `Directory.Build.props` / `Directory.Packages.props` (here and in `tests/backend/barcode-master-api/`) stop the root build settings and Central Package Management from applying; keep those copies identical and never add this service's packages to the root `Directory.Packages.props`.

## Layout

```text
src/backend/barcode-master-api/
  BarcodeAPI.sln                 # also includes the test project
  AGENTS.md                      # agent notes for this service
  architecture_doc/architecture.md
  BarcodeAPI.Api/                # host: Program.cs, Controllers, Contracts, Authentication, Authorization
  BarcodeAPI.Application/        # CreateProductCommand / CreateProductHandler
  BarcodeAPI.Domain/             # Product, BaseEntity, IProductRepository, IOpenFoodFactsClient
  BarcodeAPI.Infrastructure/     # ApplicationDbContext, Migrations, ProductRepository, OpenFoodFacts client
tests/backend/barcode-master-api/
  BarcodeAPI.Api.Tests/          # xUnit tests
```

## Endpoints

All under `api/products`, all requiring the `DualAuth` policy (JWT Bearer or `X-API-Key`):

- `GET /api/products/{barcode}` — local lookup, then Open Food Facts fallback
- `POST /api/products/lookup` — batch lookup in the local database only
- `POST /api/products/batch` — batch insert with per-record failure reporting

## Configuration

The committed `appsettings.json` contains only logging and `OpenFoodFacts`. Everything else must come from `appsettings.Development.json` (gitignored), user secrets or environment variables.

| Key | Purpose |
|---|---|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string for `ApplicationDbContext` |
| `Jwt:Secret` | Symmetric signing key used to validate JWTs (no default) |
| `Jwt:Issuer`, `Jwt:Audience` | Expected issuer and audience |
| `Authentication:ApiKey` | Key accepted in the `X-API-Key` header; API key auth fails while it is unset |
| `OpenFoodFacts:BaseUrl` | Open Food Facts base URL (default `https://world.openfoodfacts.net/`) |
| `OpenFoodFacts:TimeoutSeconds` | HTTP timeout for the fallback call (default `10`) |

## Build, run, test

From the repository root:

```bash
dotnet build src/backend/barcode-master-api/BarcodeAPI.sln
dotnet run --project src/backend/barcode-master-api/BarcodeAPI.Api
dotnet test src/backend/barcode-master-api/BarcodeAPI.sln
```

To run only the test project: `dotnet test tests/backend/barcode-master-api/BarcodeAPI.Api.Tests`. The tests use in-memory fakes and a stub HTTP handler; they need neither Docker nor a database.

Migrations are not applied at startup. Apply them with:

```bash
dotnet ef database update \
  --project src/backend/barcode-master-api/BarcodeAPI.Infrastructure \
  --startup-project src/backend/barcode-master-api/BarcodeAPI.Api
```

`Properties/launchSettings.json` is gitignored, so the local port depends on your own launch profile or `ASPNETCORE_URLS`. The OpenAPI document is mapped only in the Development environment.

## CI

[`.github/workflows/barcode-master-api-ci.yml`](../../../.github/workflows/barcode-master-api-ci.yml) restores, builds (`Release`) and runs `dotnet test` on `BarcodeAPI.sln` for pull requests to and pushes on `main` that touch `src/backend/barcode-master-api/**`, `tests/backend/barcode-master-api/**`, the workflow file, or `global.json`. The repository has no Dockerfile or deployment workflow for this service.

## Docs

- [architecture_doc/architecture.md](architecture_doc/architecture.md) — layers, runtime composition, endpoint flows, auth, data model, constraints
- [AGENTS.md](AGENTS.md) — short working notes for AI agents
