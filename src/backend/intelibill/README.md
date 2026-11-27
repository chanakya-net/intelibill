# Merchant Backend (`Intelibill.Api`)

The merchant API of intelibill: multi-shop inventory, billing, purchasing, credit, tills and offline-sale reconciliation for shop owners and their staff. It serves the merchant web app ([`src/frontend/intelibill`](../../frontend/intelibill/README.md)) and the Android app ([`src/mobile/android/intelibill_mobile`](../../mobile/android/intelibill_mobile/README.md)).

## Stack

ASP.NET Core 10 (controllers + SignalR hubs), C# latest, EF Core 10 on PostgreSQL (Npgsql, row-level security), Wolverine 5.24 handlers, FluentValidation 12, ErrorOr 2.0, Serilog. The .NET SDK is pinned in the repo-root `global.json`; package versions live in the repo-root `Directory.Packages.props` (Central Package Management).

## Layout

Dependencies flow inward: `Domain ← Application ← Infrastructure ← Api`. Each layer registers itself through its `DependencyInjection.cs`.

| Path | Purpose |
|---|---|
| `Intelibill.Domain/` | Entities, value objects, domain interfaces. No dependencies. |
| `Intelibill.Application/` | Wolverine handlers and FluentValidation validators under `Features/<Area>/`, shared errors in `Common/Errors/Errors.cs`. |
| `Intelibill.Infrastructure/` | EF Core DbContexts, `IEntityTypeConfiguration<T>` mappings, repositories, migrations, options. |
| `Intelibill.Api/` | Controllers, SignalR hubs, middleware, health checks, startup. |
| `Intelibill.slnx` | Solution: the four projects, the shared libraries and the merchant test projects. |
| `Dockerfile`, `Dockerfile.migrate` | API image and EF migration-bundle image. |
| `architecture_doc/` | Architecture and pattern docs for this project. |

Shared libraries used by this API live in [`src/backend/shared`](../shared/README.md). Tests live in `tests/backend/intelibill/` (`unit/` and `integration/`).

## Build, run, test

Run from the repository root.

```bash
dotnet build src/backend/intelibill/Intelibill.slnx
dotnet run --project src/backend/intelibill/Intelibill.Api
```

The API listens on `http://localhost:5277` (`Properties/launchSettings.json`). `appsettings.json` is intentionally empty; local settings, including the database connection, are in `Intelibill.Api/appsettings.Development.json`. A local PostgreSQL is available through `docker compose up -d` at the repository root.

```bash
# Unit tests
dotnet test tests/backend/intelibill/unit/Intelibill.Domain.Unit.Tests
dotnet test tests/backend/intelibill/unit/Intelibill.Application.Unit.Tests
dotnet test tests/backend/intelibill/unit/Intelibill.Api.Unit.Tests

# Integration tests (Docker required: Testcontainers PostgreSQL)
scripts/dev/test-integration.sh [--filter "..."]
```

## Migrations

The API never applies migrations at startup. Apply them yourself locally; deployed environments use the migration job.

```bash
dotnet ef migrations add <MigrationName> \
  --project src/backend/intelibill/Intelibill.Infrastructure \
  --startup-project src/backend/intelibill/Intelibill.Api

dotnet ef database update \
  --project src/backend/intelibill/Intelibill.Infrastructure \
  --startup-project src/backend/intelibill/Intelibill.Api
```

The context starts from one `Baseline` migration that runs an embedded `pg_dump` script (`Migrations/*_Baseline.sql`). Never edit it; add new migrations after it.

## Conventions

- ErrorOr results, not exceptions, for expected failures; FluentValidation for requests.
- Repository abstractions plus `IUnitOfWork` for writes; no data annotations on entities.
- Warnings are errors: every build must be analyzer-clean.
- Tenant tables are protected by PostgreSQL RLS keyed on `app.current_user_id` and `app.active_shop_id`.
- Never log exception messages, tokens, links or DTOs by hand; logging goes through `LogEventRedactor`.

## Documentation

- [Architecture](architecture_doc/architecture.md): tenancy, auth and sessions, rate limiting, safe errors, financial operations, stock and purchase receipts, returns, credit, posting periods, offline reconciliation, tills, expiry, test structure, migration baselines.
- [Patterns](architecture_doc/patterns.md): implementation patterns with canonical file references.
- Rollout, setup and operations notes that span projects are in the repo-root [`docs/`](../../../docs).
