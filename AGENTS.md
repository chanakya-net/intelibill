# intelibill — AI inventory management system backend

## Build & Test

**Backend** (repo root, solution: `src/backend/Intelibill.slnx`):
- Build: `dotnet build src/backend/Intelibill.slnx`
- Run: `dotnet run --project src/backend/Intelibill.Api`
- Test full: `dotnet test src/backend/Intelibill.slnx`
- Test unit: `dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests`, `Application.Unit.Tests`, `Api.Unit.Tests`
- Test integration: `dotnet test tests/backend/integration/Intelibill.Integration.Tests` (requires Docker)
- Snapshot: 83 + 330 + 139 + 115 = 667 tests (552+ non-integration passing)

**Migrations** (EF Core):
```bash
dotnet ef migrations add <MigrationName> \
  --project src/backend/Intelibill.Infrastructure \
  --startup-project src/backend/Intelibill.Api
dotnet ef database update \
  --project src/backend/Intelibill.Infrastructure \
  --startup-project src/backend/Intelibill.Api
```

**Frontend** (`src/frontend`, Bun only—no npm/yarn):
- Install: `bun install`
- Start: `bun run start`
- Build: `bun run build`
- Test: `bun run test`

## Architecture

**Layer order**: Domain ← Application ← Infrastructure ← Api

| Layer | Deps | Purpose |
|---|---|---|
| **Domain** | None | Entities, value objects, domain interfaces |
| **Application** | Domain | Wolverine handlers, validators, errors (`Application/Common/Errors/Errors.cs`) |
| **Infrastructure** | Domain + App | EF Core DbContext, repos, configs in `Infrastructure/Data/Configurations` |
| **Api** | App + Infra | ASP.NET Core, controllers, middleware |

Dependency registration: each layer's `DependencyInjection.cs`

**Conventions**:
- ErrorOr result pattern (no exception-driven control flow)
- FluentValidation for requests
- Repository abstractions + `IUnitOfWork` for writes
- No data annotations on entities
- C# warnings = errors (analyzer-clean builds)
- EF: `IEntityTypeConfiguration<T>` for mappings
- Central Package Management (CPM): versions in `Directory.Packages.props` only, csproj entries versionless
- Angular: standalone components, NgRx root/feature stores, PrimeNG styling
- i18n: `src/frontend/public/assets/i18n` locale files

## Multi-Shop Tenancy & Auth

- RLS-enforced row isolation per `active_shop_id`
- JWT carries `active_shop_id` claim; switching returns new scoped token
- Auth methods: email + phone registration, OAuth (Google/Facebook), password reset, token refresh/revoke
- Shop memberships: roles = `Owner`, `Manager`, `Staff`
- Rate limiting: `RateLimitAttribute`/`RateLimitFilter` on sensitive endpoints
- Logging: Serilog + `SensitiveDataDestructuringPolicy`

## Environment

- .NET SDK: pinned via `global.json` (10.0.105, latestMinor roll-forward)
- Local DB: PostgreSQL, creds in `src/backend/Intelibill.Api/appsettings.Development.json` or `docker compose up -d`
- `appsettings.json`: intentionally empty (safe to commit)
- Production: env vars or secrets manager

DB Options: `src/backend/Intelibill.Infrastructure/Options/DatabaseOptions.cs:7`, Pattern in `"Database"` config section

## Tech Stack

| Layer | Tech |
|---|---|
| Backend | ASP.NET Core 10, C# latest, .NET 10.0.105 |
| Database | PostgreSQL + Npgsql + EF Core 10 |
| Messaging | Wolverine 5.24 (CQRS) |
| Validation | FluentValidation 12 |
| Errors | ErrorOr 2.0 |
| Tests | xUnit 2.9 + coverlet |
| Frontend | Angular 21 (`src/frontend/`) |
| Mobile | .NET MAUI (scaffolding only) |

## Current State (May 2026)

**Features**: Full inventory system (items, batches, sales, expenses, customers, suppliers, bank accounts)

**Key endpoints**:
- Auth: `POST register/email`, `register/phone`, `login/email`, `login/external/{init,callback}`, `password-reset/{request,confirm}`, `token/{refresh,revoke}`
- Shops: `GET me`, `{shopId}`, `POST`, `POST switch`, `PUT {shopId}`
- Items: `GET stream` (SSE), `GET`, `GET details`, `POST`, `PATCH {itemId}`
- Inventory: `POST inbound`, `inbound/batch`, `GET batches`, `batches/available`, `PUT/POST/void batches/{batchId}`
- Sales: `POST`, `GET`, `GET {saleId}`, `GET profit-loss`
- Customers: CRUD + `account`, `payments`
- Suppliers: CRUD + `ledger`, `payments`
- Expenses: `GET`, `POST`, `{id}`, `{id}/correct`, `categories`
- Bank Accounts: CRUD
- Users: `GET` (shop), `POST` (add), `PUT {userId}`, `me/change-password`

**Infrastructure**:
- Migration: `20260425075353_InitialCreate` (single consolidated)
- RLS: session interceptor sets `app.current_user_id` + `app.active_shop_id`
- Tests: Testcontainers (real PostgreSQL via Docker)

## Domain Model

**Entities**: `User`, `UserExternalLogin`, `Shop`, `ShopMembership`, `Item`, `Inventory`, `InventoryBatch`, `StockTransaction`, `Sale`, `SaleItem`, `Customer`, `CustomerLedgerEntry`, `Supplier`, `SupplierLedgerEntry`, `Expense`, `ExpenseCategory`, `BankAccount`, `RefreshToken`, `PasswordResetToken`

**Enums**: `ShopRole`, `PaymentMethod`, `BankAccountType`, `StockTransactionType`, `SupplierLedgerEntryType`, `CustomerLedgerEntryType`, `ExternalAuthProvider`, `SupplierStatus`

## Structure

| Path | Purpose |
|---|---|
| `src/backend/Intelibill.Domain/` | Entities, value objects, domain interfaces (zero deps) |
| `src/backend/Intelibill.Application/` | Handlers, validators (depends: Domain only) |
| `src/backend/Intelibill.Infrastructure/` | DbContext, repos, configs (depends: Domain + Application) |
| `src/backend/Intelibill.Api/` | Controllers, middleware (depends: Application + Infrastructure) |
| `tests/backend/unit/` | Domain + Application unit tests |
| `tests/backend/integration/` | Integration tests vs API |
| `Directory.Build.props` | Shared MSBuild (nullable, warnings-as-errors, analysis, CPM) |
| `Directory.Packages.props` | Central Package Management (all NuGet versions) |
| `global.json` | SDK pin (10.0.105, latestMinor roll-forward) |

## NuGet Packages

Add version to `Directory.Packages.props`:
```xml
<PackageVersion Include="Pkg.Name" Version="X.Y.Z" />
```
Add to `.csproj` (no version):
```xml
<PackageReference Include="Pkg.Name" />
```

## Graphify Knowledge Graph

Graph at `graphify-out/`

**Before architecture/codebase Q**: read `graphify-out/GRAPH_REPORT.md` (god nodes, communities) + `graphify-out/wiki/index.md` (if exists)

**Workflow**: `/graphify` → read GRAPH_REPORT.md → use graphify tools (not CodeGraph unless asked)

**After changes**: `graphify update .` (AST-only, no API cost)

**Direct lookups**: `rg`, `rg --files`, file reads OK

## Links (Reference—Don't Duplicate)

- README.md
- AGENTS.md (this file)
- docs/architectural_patterns.md
- docs/backend-architecture.md
- docs/frontend-architecture.md
- Directory.Build.props
- Directory.Packages.props
- global.json
