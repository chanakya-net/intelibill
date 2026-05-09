# intelibill

AI-powered inventory management system backend.

## Graphify

Use Graphify instead of CodeGraph for graph-based codebase exploration.

- Prefer the `graphify` workflow for architecture review, dependency mapping, broad context gathering, and knowledge graph work.
- Do not use CodeGraph tools unless the user explicitly asks for CodeGraph or Graphify is unavailable.
- If the user types `/graphify`, invoke the `graphify` skill before doing anything else.
- For small direct lookups, normal repo tools like `rg`, `rg --files`, and file reads are acceptable.
- When Graphify output exists, consult `graphify-out/GRAPH_REPORT.md` and `graphify-out/wiki/index.md` when relevant before broad architecture answers.
- After modifying code files in a session that relies on Graphify, run `graphify update .` when practical to keep the graph current.

## Current Backend Snapshot (April 2026)

- Full inventory management system: items, inventory batches, sales, expenses, customers, suppliers, bank accounts.
- Multi-shop tenancy: memberships with per-shop role (`Owner`, `Manager`, `Staff`), default shop + active shop switching.
- JWT carries `active_shop_id`; shop switch returns new scoped token; RLS enforces row isolation.
- Auth: email + phone registration, external OAuth (Google/Facebook), password reset, token refresh/revoke.
- Rate limiting middleware (`RateLimitAttribute` / `RateLimitFilter`) on sensitive endpoints.
- Structured logging via Serilog with `SensitiveDataDestructuringPolicy`.
- Items catalog streaming endpoint (`GET /api/items/stream`).
- Profit/loss report endpoint (`GET /api/sales/profit-loss`).
- Integration tests use Testcontainers (real PostgreSQL via Docker).

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

Paths relative to repo root.

| Path | Purpose |
|---|---|
| `/backend/Intelibill.Domain/` | Entities, value objects, domain interfaces — zero dependencies |
| `/backend/Intelibill.Application/` | Wolverine handlers, validators, error definitions — depends on Domain only |
| `/backend/Intelibill.Infrastructure/` | EF Core DbContext, repositories, database options — depends on Domain + Application |
| `/backend/Intelibill.Api/` | ASP.NET Core host, controllers, middleware — depends on Application + Infrastructure |
| `tests/backend/unit/` | Domain and Application unit tests |
| `tests/backend/integration/` | Integration tests referencing the API project |
| `Directory.Build.props` | Shared MSBuild properties: nullable, warnings-as-errors, analysis level, CPM flag |
| `Directory.Packages.props` | Central Package Management — all NuGet versions declared here |
| `global.json` | Pins SDK to 10.0.105 with `latestMinor` roll-forward |

## Domain Model

### Entities

| Entity | Notes |
|---|---|
| `User` | Auth identity; tracks shop memberships, external logins, refresh tokens, password reset tokens |
| `UserExternalLogin` | OAuth provider link (Google, Facebook) |
| `Shop` | Tenant root — address, GST, contact |
| `ShopMembership` | User ↔ Shop with `ShopRole` |
| `Item` | Catalog entry (name, barcode, tax rates) |
| `Inventory` | Stock record per item per shop |
| `InventoryBatch` | Purchase batch — cost price, qty, supplier, expiry |
| `StockTransaction` | Append-only stock movement log |
| `Sale` | Sale header (payment method, discount, customer) |
| `SaleItem` | Line item — links to batch, records cost/price at time of sale |
| `Customer` | Customer profile |
| `CustomerLedgerEntry` | Credit/payment ledger per customer |
| `Supplier` | Supplier profile |
| `SupplierLedgerEntry` | Purchase/payment ledger per supplier |
| `Expense` | Shop expense with category |
| `ExpenseCategory` | Expense taxonomy |
| `BankAccount` | Bank account reference per shop |
| `RefreshToken` | JWT refresh token |
| `PasswordResetToken` | One-time password reset token |

### Key Enums

`ShopRole`, `PaymentMethod`, `BankAccountType`, `StockTransactionType`, `SupplierLedgerEntryType`, `CustomerLedgerEntryType`, `ExternalAuthProvider`, `SupplierStatus`

## Infrastructure

- Migration: `20260425075353_InitialCreate` (single consolidated migration)
- RLS session context interceptor sets `app.current_user_id` + `app.active_shop_id` per request
- Integration tests use **Testcontainers** (`Testcontainers.PostgreSql`) — Docker required

## API Endpoints

### Auth — `api/auth`
- `POST register/email`, `POST register/phone`
- `POST login/email`, `POST login/external`, `POST login/external/init`, `POST login/external/callback`
- `GET ~/auth/google/callback`, `GET ~/auth/facebook/callback`
- `POST password-reset/request`, `POST password-reset/confirm`
- `POST token/refresh`, `POST token/revoke`

### Shops — `api/shops`
- `GET me`, `GET {shopId}`, `POST`, `POST switch`, `POST default`, `PUT {shopId}`

### Items — `api/items`
- `GET stream` (SSE), `GET`, `GET details`, `POST`, `PATCH {itemId}`

### Inventory — `api/inventory`
- `POST inbound`, `POST inbound/batch`
- `GET batches`, `GET batches/available`
- `PUT batches/{batchId}`, `POST batches/{batchId}/void`, `POST batches/{batchId}/reassign-supplier`

### Sales — `api/sales`
- `POST`, `GET`, `GET {saleId}`, `GET profit-loss`

### Customers — `api/customers`
- `GET`, `POST`, `PUT {customerId}`, `GET {customerId}/account`, `POST {customerId}/payments`

### Suppliers — `api/suppliers`
- `GET`, `POST`, `PUT {supplierId}`, `DELETE {supplierId}`
- `GET {supplierId}/ledger`, `POST {supplierId}/payments`

### Expenses — `api/expenses`
- `GET`, `POST`, `GET {id}`, `POST {id}/correct`, `GET categories`

### Bank Accounts — `api/bank-accounts`
- `GET`, `POST`, `PUT {id}`, `DELETE {id}`

### Users — `api/users`
- `GET` (shop users), `POST` (add shop user), `PUT {targetUserId}`
- `POST me/change-password`, `PUT me`

## Build & Test

Commands from **repo root** unless noted. Solution: `src/backend/Intelibill.slnx`.

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

Test snapshot:

- `Intelibill.Domain.Unit.Tests`: 83
- `Intelibill.Application.Unit.Tests`: 330
- `Intelibill.Api.Unit.Tests`: 139
- `Intelibill.Integration.Tests`: 115 *(requires Docker)*
- Total: 552+ passing (non-integration)

## Configuration

DB credentials use Options Pattern bound to `"Database"` config section.
See `src/backend/Intelibill.Infrastructure/Options/DatabaseOptions.cs:7`.

- `src/backend/Intelibill.Api/appsettings.json` — intentionally empty strings; safe to commit
- `src/backend/Intelibill.Api/appsettings.Development.json` — local defaults (`localhost:5432/inventoryai_dev`)
- Production — supply via env vars or secrets manager

## Adding NuGet Packages

1. Add `<PackageVersion Include="..." Version="..." />` to `Directory.Packages.props`
2. Add `<PackageReference Include="..." />` (no version) to relevant `.csproj`

## Additional Documentation

| Topic | File |
|---|---|
| Architecture, design patterns, conventions | [docs/architectural_patterns.md](docs/architectural_patterns.md) |
