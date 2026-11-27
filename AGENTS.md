# intelibill — Agent Guide

`CLAUDE.md` and `GEMINI.md` symlink to this file. Keep this guide concise; put architecture docs in `architecture_doc/` and detailed subsystem/rollout notes in `docs/`, and link them below.

## Build & Test

**Merchant Backend** (`src/backend/Intelibill.slnx`):
- Build: `dotnet build src/backend/Intelibill.slnx`
- Run: `dotnet run --project src/backend/Intelibill.Api`
- Unit tests: `dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests` (also `Application.Unit.Tests`, `Api.Unit.Tests`)
- Integration tests (requires Docker): `scripts/dev/test-integration.sh [--filter "..."]` (pre-generates Wolverine handlers, runs against Testcontainers PostgreSQL, then cleans generated files)

**Licensing Service** (`src/licensing/Intelibill.Licensing.slnx`):
- Build: `dotnet build src/licensing/Intelibill.Licensing.slnx`
- Integration tests (requires Docker): `scripts/dev/test-integration.sh --licensing [--filter "..."]` (reuses cached generated handlers while the merchant backend is unchanged; do not set `INTEGRATION_TEST_PARALLELISM` below the default for a local run, see `docs/licensing-rollout.md`)
- Verification scoping: run targeted `--filter` suites for touched features/seams first; run the unfiltered licensing suite only when shared licensing DB, entitlement, or delegation seams change.

**EF Core Migrations** — API startup never applies migrations automatically; run `database update` locally (deployed environments use the migration job):
```bash
dotnet ef migrations add <MigrationName> --project src/backend/Intelibill.Infrastructure --startup-project src/backend/Intelibill.Api
dotnet ef database update --project src/backend/Intelibill.Infrastructure --startup-project src/backend/Intelibill.Api
```
For licensing migrations, target `--project src/licensing/Intelibill.Licensing.Infrastructure --startup-project src/licensing/Intelibill.Licensing.Api --context LicensingDbContext`.
Each context starts from one `Baseline` migration that runs an embedded, verified `pg_dump` script (`Migrations/*_Baseline.sql`); never edit it, add new migrations after it, and recreate a database instead of reverting the baseline (see `architecture_doc/backend-architecture.md`).

**Web Workspaces** (Bun `1.3.11` only — never `npm` or `yarn`; single root `bun.lock`):
- Merchant web (`src/frontend`): `bun install`, `bun run start`, `bun run build`, `bun run test --watch=false` (or `--run <spec>`)
- Admin web (`src/admin-frontend`): `bun run build`, `bun run test --watch=false`, `bun run audit:release`
- **Mandatory fast checks when touching frontend:**
  - Whenever templates, copy, or `src/frontend/public/assets/i18n/*.json` change, run `(cd src/frontend && bun run test --run src/app/core/i18n)` (validates all 9 locales).
  - Preserve style duplicate ceiling (`27`, only goes down): `(cd src/frontend && bun run audit:styles:duplicates)`.
  - Service worker / release audit: `(cd src/frontend && bun run build && bun run audit:ui:release)`. Never ship Angular's bare `ngsw-worker.js` — `bun run build` wraps it with `service-worker/protected-api-worker.js` so `/api` and `/hubs` bypass cache.

**Mobile** (`src/mobile/android/intelibill_mobile`, Flutter Android only, `.flutter-version` `3.44.2`):
- Setup & codegen: `flutter pub get`, `./tool/codegen.sh` (commit `lib/**/*.g.dart`, `lib/**/*.freezed.dart`, `lib/src/core/localization/`)
- Verify: `flutter analyze`, `dart format --set-exit-if-changed .`, `flutter test --coverage`, `flutter build apk --debug`

## Architecture & Conventions

**Layer order**: `Domain ← Application ← Infrastructure ← Api` (each layer registers via `DependencyInjection.cs`).

| Layer | Deps | Purpose |
|---|---|---|
| **Domain** | None | Entities, value objects, domain interfaces |
| **Application** | Domain | Wolverine handlers, FluentValidation validators, errors (`Application/Common/Errors/Errors.cs`) |
| **Infrastructure** | Domain + App | EF Core DbContexts, repositories, `IEntityTypeConfiguration<T>` mappings |
| **Api** | App + Infra | ASP.NET Core controllers, endpoints, middleware |

**Hard conventions**:
- ErrorOr result pattern (no exception-driven control flow) and FluentValidation for requests.
- Repository abstractions + `IUnitOfWork` for writes; no data annotations on entities; `IEntityTypeConfiguration<T>` for EF mappings.
- C# warnings are errors (`Directory.Build.props`) — all builds must be analyzer-clean.
- Central Package Management (`Directory.Packages.props` only; `.csproj` `<PackageReference>` entries must be versionless).
- Angular 21 standalone components, NgRx root/feature stores, PrimeNG + shared `@intelibill/ui` and `@intelibill/api` workspace packages, Transloco i18n (`src/frontend/public/assets/i18n`).
- Mobile: feature-first clean architecture (domain-independent), Riverpod codegen, GoRouter, Dio, Freezed/Json Serializable, Very Good Analysis.

## Security, Tenancy & Runtime Invariants

- **Multi-shop RLS**: session interceptor sets `app.current_user_id` and `app.active_shop_id`; tenant tables enforce PostgreSQL RLS.
- **Auth & sessions**: verified email/phone credentials only (no external IdP). Every access JWT carries `active_shop_id` and `AuthSession` `sid` (tokens without `sid` are refused). Refresh tokens are single-use SHA-256 hashes chained per session; roles are `Owner`, `Manager`, `Staff`.
- **Rate limiting**: `RateLimitAttribute` / `RateLimitFilter` over `IAtomicRateLimitStore` (`rate_limit_buckets`); auth endpoints answer `503` when the store is down.
- **Safe logging & errors**: Serilog sinks sit behind `LogEventRedactor` (`Intelibill.Shared.Web`). Never log exception messages, tokens, links, or DTOs by hand. Unhandled/API errors answer `{type,title,status,code,traceId,errors?}` via `SafeErrorFactory` and `SafeDiagnosticWriter`.
- **Tests**: Testcontainers real PostgreSQL via Docker; integration classes run in parallel on per-class template databases (`SerialIntegrationTestGroup` for serial-only classes).

## Environment & Tech Stack

- **.NET**: SDK pinned in `global.json` (`10.0.105`, `latestMinor` roll-forward); ASP.NET Core 10, C# latest, EF Core 10, Npgsql, Wolverine 5.24, FluentValidation 12, ErrorOr 2.0, xUnit 2.9.
- **Database**: PostgreSQL (`src/backend/Intelibill.Api/appsettings.Development.json` or `docker compose up -d`; `DatabaseOptions` in `src/backend/Intelibill.Infrastructure/Options/DatabaseOptions.cs`). `appsettings.json` is intentionally empty.
- **Web / Mobile**: Angular 21 + PrimeNG 21 + Bun 1.3.11 + Vitest + Playwright; Flutter 3.44.2.

## Structure

| Path | Purpose |
|---|---|
| `src/backend/` | Merchant API (`Domain`, `Application`, `Infrastructure`, `Api`) |
| `src/licensing/` | Licensing service (`Intelibill.Licensing.slnx`) |
| `src/frontend/` | Merchant Angular web app |
| `src/admin-frontend/` | Admin Angular web app (`ADMIN`) |
| `src/shared/frontend/` | Shared web workspace packages (`@intelibill/ui`, `@intelibill/api`) |
| `src/mobile/android/intelibill_mobile/` | Flutter Android app |
| `src/services/` | Separate deployables (`barcode-master-api`, `hsn-finder-ai`) |
| `tests/backend/` & `tests/licensing/` | Unit & Testcontainers integration tests |
| `Directory.Build.props`, `Directory.Packages.props`, `global.json` | Root MSBuild, CPM, and .NET SDK pin |

## NuGet Packages & Supporting Services

- Root projects: add `<PackageVersion Include="Pkg.Name" Version="X.Y.Z" />` to `Directory.Packages.props` and versionless `<PackageReference Include="Pkg.Name" />` to `.csproj`.
- **Supporting services (`src/services/`, `tests/services/`)**: `barcode-master-api` (`BarcodeAPI.sln`, own `AGENTS.md`) and `hsn-finder-ai` (`Sankra.HsnFinder.sln`) are separate deployables excluded from `Intelibill.slnx`. Their `Directory.Build.props` / `Directory.Packages.props` disable root CPM — keep both copies identical and **never** add service packages to the root `Directory.Packages.props`.

## CodeGraph

`.codegraph/` holds the repository's code knowledge graph:
- Use `codegraph_context` first for broad codebase tasks, `codegraph_search` / `codegraph_node` for symbols, `codegraph_callers` / `codegraph_callees` to trace execution, and `codegraph_impact` before changing shared symbols.
- Use `rg`, `rg --files`, and direct file reads for config, docs, and non-symbol searches.
- If `.codegraph/` is missing, ask before running `codegraph init -i`.

## Documentation Index (Read Before Editing a Subsystem)

- `README.md` — overview & local setup
- `architecture_doc/architectural_patterns.md` — shared design & code patterns
- `architecture_doc/backend-architecture.md` — tenancy, auth, financial ops, credit, returns, stock/purchase receipts, posting periods, offline sync/reconciliation, locking orders, integration parallelism
- `architecture_doc/frontend-architecture.md` — shop context transitions, protected API service worker, offline pack & printing, shared styles
- `architecture_doc/mobile-architecture.md` — Flutter app architecture
- `architecture_doc/infrastructure-architecture.md` — Azure/OpenTofu infrastructure architecture
