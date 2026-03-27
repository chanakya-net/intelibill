# intelibill

AI-powered inventory management system backend.

## Current Backend Snapshot (March 2026)

- Multi-shop tenancy support is implemented.
- A user can have memberships across many shops with per-shop role (`Owner`, `Manager`, `Staff`).
- Default shop and active shop switching are supported.
- JWT now carries `active_shop_id`; switching shop returns a new scoped token.
- PostgreSQL migration and RLS policies are in place for `shops` and `shop_memberships`.
- Error mapping now includes `Forbidden -> 403`.
- Test suite includes expanded domain, API, application, and integration coverage.

## Tech Stack

| Layer | Technology |
|---|---|
| Backend API | ASP.NET Core 10, C# latest, .NET 10.0.105 |
| Database | PostgreSQL via Npgsql + EF Core 10 |
| Messaging / CQRS | Wolverine 5.24 |
| Validation | FluentValidation 12 |
| Error handling | ErrorOr 2.0 (result pattern) |
| Tests | xUnit 2.9 + coverlet |
| Frontend | Angular 21 (separate workspace under `src/frontend/`) |
| Mobile | .NET MAUI *(scaffolding only)* |

## Key Directories

Paths are relative to this file (`src/backend/`).

| Path | Purpose |
|---|---|
| `Intelibill.Domain/` | Entities, value objects, domain interfaces — zero dependencies |
| `Intelibill.Application/` | Wolverine handlers, validators, error definitions — depends on Domain only |
| `Intelibill.Infrastructure/` | EF Core DbContext, repositories, database options — depends on Domain + Application |
| `Intelibill.Api/` | ASP.NET Core host, controllers, middleware — depends on Application + Infrastructure |
| `../../tests/backend/unit/` | Domain and Application unit tests |
| `../../tests/backend/integration/` | Integration tests referencing the API project |
| `../../Directory.Build.props` | Shared MSBuild properties: nullable, warnings-as-errors, analysis level, CPM flag |
| `../../Directory.Packages.props` | Central Package Management — all NuGet versions are declared here |
| `../../global.json` | Pins SDK to 10.0.105 with `latestMinor` roll-forward |

## Multi-Shop Architecture Notes

### Domain

- `Shop`
- `ShopMembership`
- `ShopRole` enum
- `User` now tracks shop memberships

### Application

- Shop commands and query:
  - create shop
  - switch active shop
  - set default shop
  - get my shops
- Auth responses include:
  - `activeShopId`
  - list of accessible shops

### Infrastructure

- Migration: `20260327181741_AddShopIsolation`
- Tables: `shops`, `shop_memberships`
- RLS policies:
  - `shop_memberships_user_policy`
  - `shops_membership_policy`
- Session context interceptor sets:
  - `app.current_user_id`
  - `app.active_shop_id`

### API Endpoints

- `GET /api/shops/me`
- `POST /api/shops`
- `POST /api/shops/switch`
- `POST /api/shops/default`

## Build & Test

Commands run from the **repo root** unless noted. Solution file: `src/backend/Intelibill.slnx`.

```bash
# Build
dotnet build src/backend/Intelibill.slnx

# Run API (picks up appsettings.Development.json automatically)
dotnet run --project src/backend/Intelibill.Api

# Test — full solution
dotnet test src/backend/Intelibill.slnx

# Test — individual projects
dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests
dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests
dotnet test tests/backend/integration/Intelibill.Integration.Tests

# EF Core migrations
dotnet ef migrations add <MigrationName> \
  --project src/backend/Intelibill.Infrastructure \
  --startup-project src/backend/Intelibill.Api

dotnet ef database update \
  --project src/backend/Intelibill.Infrastructure \
  --startup-project src/backend/Intelibill.Api
```

Current passing test snapshot:

- `Intelibill.Domain.Unit.Tests`: 22
- `Intelibill.Application.Unit.Tests`: 25
- `Intelibill.Api.Unit.Tests`: 22
- `Intelibill.Integration.Tests`: 2
- Total: 71 passing

## Configuration

Database credentials use the Options Pattern bound to the `"Database"` config section.
See `Intelibill.Infrastructure/Options/DatabaseOptions.cs:7`.

- `Intelibill.Api/appsettings.json` — intentionally empty strings; safe to commit
- `Intelibill.Api/appsettings.Development.json` — local defaults (`localhost:5432/inventoryai_dev`)
- Production — supply values via environment variables or secrets manager

## Adding NuGet Packages

1. Add `<PackageVersion Include="..." Version="..." />` to `../../Directory.Packages.props`
2. Add `<PackageReference Include="..." />` (no version) to the relevant `.csproj`

## Additional Documentation

| Topic | File |
|---|---|
| Architecture, design patterns, conventions | [../../.claude/docs/architectural_patterns.md](../../.claude/docs/architectural_patterns.md) |
