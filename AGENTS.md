# intelibill — AI inventory management system backend

## Build & Test

**Backend** (repo root, solution: `src/backend/Intelibill.slnx`):
- Build: `dotnet build src/backend/Intelibill.slnx`
- Run: `dotnet run --project src/backend/Intelibill.Api`
- Test full: `dotnet test src/backend/Intelibill.slnx`
- Test unit: `dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests`, `Application.Unit.Tests`, `Api.Unit.Tests`
- Test integration: `dotnet test tests/backend/integration/Intelibill.Integration.Tests` (requires Docker)
- Snapshot: 265 + 1057 + 542 + 575 = 2439 tests (1864 non-integration), all passing as of 3 November 2026

**Migrations** (EF Core) — API startup does **not** apply them; run `database update` yourself after pulling schema changes (deployed environments use the migration job):
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

**Mobile** (`src/mobile/android/intelibill_mobile`, Flutter Android only):
- Flutter: pin to `.flutter-version` (3.44.2); FVM optional (`fvm use` in mobile dir)
- Setup: `flutter pub get`
- Generate: `./tool/codegen.sh` (l10n + build_runner + format; commit `lib/**/*.g.dart`, `lib/**/*.freezed.dart`, `lib/src/core/localization/`)
- Analyze: `flutter analyze`
- Format: `dart format --set-exit-if-changed .`
- Test: `flutter test --coverage`
- Build: `flutter build apk --debug`

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
- Mobile: Feature-first clean architecture (domain-independent), Riverpod generated providers, GoRouter, Dio, Freezed/Json Serializable, flutter_secure_storage + shared_preferences, Very Good Analysis

## Multi-Shop Tenancy & Auth

- RLS-enforced row isolation per `active_shop_id`
- JWT carries `active_shop_id` claim; switching returns new scoped token
- Auth methods: verified email enrollment, verified phone enrollment and login, password reset, token refresh/revoke. No external identity provider: credentials are the only way in.
- Device sessions: every access token names an `AuthSession` (`sid`); only a credential check creates one, and a session can be revoked on its own. A token with no `sid` is refused.
- Refresh credentials: stored as SHA-256 only, single-use, chained per session (`parent_token_id`). Rotation and sign-out lock users → auth_sessions → refresh_tokens; a reused (consumed) credential ends its session. Web tabs serialize renewals via `RefreshCoordinator`. See `docs/backend-architecture.md` § Refresh Credentials.
- Shop memberships: roles = `Owner`, `Manager`, `Staff`
- Rate limiting: `RateLimitAttribute`/`RateLimitFilter` over `IAtomicRateLimitStore` (`rate_limit_buckets`, one atomic transaction across replicas); auth endpoints add account/destination budgets via `IRateLimitSubject` and answer 503 when the store is down. See `docs/backend-architecture.md` § Rate Limiting
- Logging: Serilog; every sink sits behind `LogEventRedactor` (`SerilogExtensions.Redacted`): exception objects become type/frame descriptions, credential names are masked at any depth (PII too with `EnablePiiMasking`). Never log an exception message, token, link or DTO by hand
- Errors: exceptions answer `{type,title,status,code,traceId,errors?}` via `SafeErrorFactory` (`Internal.Unexpected` for anything unplanned) and log through `SafeDiagnosticWriter`; `SafeSpanProcessor` strips exception text from exported spans. See `docs/backend-architecture.md` § Safe Errors and Diagnostics

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
| Mobile | Flutter Android (`src/mobile/android/intelibill_mobile/`) |

## Current State (May 2026)

**Features**: Full inventory system (items, batches, sales, expenses, customers, suppliers, bank accounts)

**Key endpoints**:
- Auth: `POST register/email`, `login/email`, `password-reset/{request,confirm}`, `token/{refresh,revoke}`, `enrollment/email/{request,confirm}`, `enrollment/email/claim/{request,confirm}`, `reauthenticate`, `email-change/{request,confirm}`, `GET sign-in-methods`. `POST register/phone` is closed: it answers 503 `Auth.PhoneEnrollmentUnavailable`
- Phone (off unless `PhoneAuth:Enabled`): `GET phone/availability`, `POST phone/{enrollment,login}/{request,confirm}`, `POST phone/change/{request,confirm}` (authenticated)
- Sessions: `GET users/me/sessions`, `DELETE users/me/sessions/{id}`, `POST users/me/sessions/revoke-all` (ends every device, the caller's included)
- Shops: `GET me`, `{shopId}`, `POST` and `POST switch` (any active member; both return an access scope, never a refresh token), `PUT {shopId}`, `PATCH {shopId}/members/{userId}`, `GET/POST {shopId}/invitations`, `DELETE {shopId}/invitations/{invitationId}`
- Items: `GET stream` (SSE), `GET`, `GET details`, `POST`, `PATCH {itemId}`
- Inventory: `POST inbound`, `inbound/batch` (optional `operationKey`; per-row commit, resend replays), `GET batches` (each with its row `version`), `batches/available`, `PUT/POST/void batches/{batchId}`, `POST batches/{batchId}/count` (owner/manager; `expectedVersion` + reason, optional `operationKey`; stale version → 409 `Inventory.BatchVersionConflict`), `PUT batches/{batchId}/prices` (owner/manager; in place, required `expectedVersion`, appends a price revision), `POST receipts/{receiptValueId}/cost-corrections` (owner; posts only the delta, optional `operationKey`). Void refuses a consumed receipt (409 `Inventory.BatchConsumed`). Legacy `PUT batches/{batchId}` never replaces the batch: quantity/cost/supplier changes answer 400 `Inventory.Batch{Quantity,Cost,Supplier}EditMoved`
- Sales: `POST`, `GET`, `GET {saleId}`, `GET profit-loss`
- Customers: CRUD + `account`, `payments` (optional `operationKey`)
- Suppliers: CRUD + `ledger`, `payments` (optional `operationKey`)
- Financial operations: `GET financial-operations/{customer-payment|supplier-payment}/{key}` (owner/manager; committed outcome or 404)
- Expenses: `GET`, `POST`, `{id}`, `{id}/correct`, `categories`
- Bank Accounts: CRUD
- Users: `GET` (shop), `PUT me` (returns the saved profile, no token), `me/change-password` (ends every device, including the caller's); `POST` (add) and `PUT {userId}` are refused — onboarding goes through shop invitations and shop administration through the membership endpoint
- Invitations: `POST invitations/{invitationId}/accept` (the invited person's own session)

**Infrastructure**:
- Verified phone: `phone_challenges` holds purpose-bound keyed-HMAC codes; `users.is_phone_verified` backfills false. Delivery via the encrypted `identity_sms` outbox and the `ISmsSender` seam. See `docs/auth-phone-setup.md` and `docs/backend-architecture.md` § Verified Phone Credentials
- Realtime: hub connections are admitted by `RealtimeConnectionGate` against live authority, recorded in `realtime_connection_leases`, addressed per payload by `IAuthorizedRealtimeDispatcher<THub>` (no SignalR groups) and closed by `RealtimeRevocationWorker`'s five-second sweep. See `docs/backend-architecture.md` § Realtime Notifications
- Financial operations: keyed payments run through `IFinancialOperationExecutor`, which reserves an `operation_receipts` row (unique per shop + type + key) and commits it with the posting; retries replay the stored response, changed payloads answer 409 `Operation.PayloadMismatch`. Bulk stock intake commits a manifest receipt plus one receipt per row through it (`IInboundRowExecutor`, fresh scope per row). See `docs/backend-architecture.md` § Financial Operation Receipts and § Bulk Stock Intake
- Migration: `20260425075353_InitialCreate` (single consolidated)
- Sale returns: record and void run in `ISaleReturnMutationCoordinator` under the customer + sale row locks (`FOR NO KEY UPDATE`, 5 s budget → 409 `SaleReturn.SaleBusy`); lock order customer → sale → credit notes → batches; `CreditNote.HasBeenRedeemed` checks balance too. See `docs/backend-architecture.md` § Sale Return Serialization
- Stock corrections: `inventory_batches`/`inventory` use `xmin` as row version; every quantity change writes one signed movement in the same save, every count a `stock_counts` row. See `docs/backend-architecture.md` § Stock Corrections and Voids
- Purchase receipts: every receipt writes a `purchase_receipt_values` row (original + effective total, `xmin` version); cost corrections post a signed `COST_ADJUSTED` ledger entry plus a `purchase_receipt_cost_adjustments` row, never touching batch cost or sale COGS; legacy receipts without clear evidence are `LEGACY_UNRESOLVED` and refuse correction. See `docs/backend-architecture.md` § Purchase Receipts, Price Revisions and Cost Corrections
- RLS: session interceptor sets `app.current_user_id` + `app.active_shop_id`
- Shop authority: `ShopMembership.Status`/`AuthorityVersion`; role and status changes, invitation issue and acceptance all run under one authority-lock order (users → shop → membership/invitation rows), and the token's shop scope is re-checked against the live membership on every request
- Tests: Testcontainers (real PostgreSQL via Docker)

## Domain Model

**Entities**: `User`, `AuthSession`, `RealtimeConnectionLease`, `PhoneChallenge`, `IdentitySms`, `Shop`, `ShopMembership`, `ShopInvitation`, `Item`, `Inventory`, `InventoryBatch`, `StockTransaction`, `Sale`, `SaleItem`, `Customer`, `CustomerLedgerEntry`, `Supplier`, `SupplierLedgerEntry`, `Expense`, `ExpenseCategory`, `BankAccount`, `RefreshToken`, `PasswordResetToken`, `OperationReceipt`, `StockCount`, `PurchaseReceiptValue`, `PurchaseReceiptCostAdjustment`, `BatchPriceRevision`

**Enums**: `ShopRole`, `MembershipStatus`, `CredentialKind`, `PaymentMethod`, `BankAccountType`, `StockTransactionType`, `SupplierLedgerEntryType`, `CustomerLedgerEntryType`, `SupplierStatus`, `FinancialOperationType`, `OperationReceiptStatus`, `PurchaseReceiptSource`, `PurchaseReceiptValueStatus`

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

## CodeGraph

`.codegraph/` is the repository's code knowledge graph.

- Use `codegraph_context` first for broad codebase tasks.
- Use `codegraph_search` and `codegraph_node` for symbol lookup.
- Use `codegraph_callers` and `codegraph_callees` to trace execution.
- Use `codegraph_impact` before changing shared symbols.
- Use `rg`, `rg --files`, and direct file reads for configuration, documentation,
  and other non-symbol searches.
- If `.codegraph/` is missing, ask before running `codegraph init -i`.

## Links (Reference—Don't Duplicate)

- README.md
- AGENTS.md (this file)
- docs/architectural_patterns.md
- docs/backend-architecture.md
- docs/frontend-architecture.md
- docs/mobile-architecture.md
- Directory.Build.props
- Directory.Packages.props
- global.json
