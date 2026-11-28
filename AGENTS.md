# intelibill — Agent Guide

`CLAUDE.md` and `GEMINI.md` are committed symlinks to this file. Keep it under 12,000 characters (Antigravity's rule-file limit). Each project owns its docs: `<project>/README.md` and `<project>/architecture_doc/`; cross-project setup, rollout and operations notes go in `docs/`. Link them in the index below.

## Build & Test

**Merchant Backend** (`src/backend/intelibill/Intelibill.slnx`):
- Build: `dotnet build src/backend/intelibill/Intelibill.slnx`
- Run: `dotnet run --project src/backend/intelibill/Intelibill.Api`
- Unit tests: `dotnet test tests/backend/intelibill/unit/Intelibill.Domain.Unit.Tests` (also `Application.Unit.Tests`, `Api.Unit.Tests`)
- Integration tests (requires Docker): `scripts/dev/test-integration.sh [--filter "..."]` (pre-generates Wolverine handlers, runs against Testcontainers PostgreSQL, then cleans generated files)

**Licensing Service** (`src/backend/licensing/Intelibill.Licensing.slnx`):
- Build: `dotnet build src/backend/licensing/Intelibill.Licensing.slnx`
- Integration tests (requires Docker): `scripts/dev/test-integration.sh --licensing [--filter "..."]` (reuses cached generated handlers while the merchant backend is unchanged; do not set `INTEGRATION_TEST_PARALLELISM` below the default for a local run, see `docs/licensing-rollout.md`)
- Verification scoping: run targeted `--filter` suites for touched features/seams first; run the unfiltered licensing suite only when shared licensing DB, entitlement, or delegation seams change.

**EF Core Migrations** — API startup never applies migrations automatically; run `database update` locally (deployed environments use the migration job):
```bash
dotnet ef migrations add <MigrationName> --project src/backend/intelibill/Intelibill.Infrastructure --startup-project src/backend/intelibill/Intelibill.Api
dotnet ef database update --project src/backend/intelibill/Intelibill.Infrastructure --startup-project src/backend/intelibill/Intelibill.Api
```
For licensing migrations, target `--project src/backend/licensing/Intelibill.Licensing.Infrastructure --startup-project src/backend/licensing/Intelibill.Licensing.Api --context LicensingDbContext`.
Each context starts from one `Baseline` migration that runs an embedded, verified `pg_dump` script (`Migrations/*_Baseline.sql`); never edit it, add new migrations after it, and recreate a database instead of reverting the baseline (see `src/backend/intelibill/architecture_doc/architecture.md` § Migration baselines).

**Web Workspaces** (Bun `1.3.11` only — never `npm` or `yarn`; single root `bun.lock`):
- Merchant web (`src/frontend/intelibill`): `bun install`, `bun run start`, `bun run build`, `bun run test --watch=false` (or `--run <spec>`)
- Admin web (`src/frontend/licensing`): `bun run build`, `bun run test --watch=false`, `bun run audit:release`
- **Mandatory fast checks when touching frontend:**
  - Whenever templates, copy, or `src/frontend/intelibill/public/assets/i18n/*.json` change, run `(cd src/frontend/intelibill && bun run test --run src/app/core/i18n)` (validates all 9 locales).
  - Preserve style duplicate ceiling (`27`, only goes down): `(cd src/frontend/intelibill && bun run audit:styles:duplicates)`.
  - Service worker / release audit: `(cd src/frontend/intelibill && bun run build && bun run audit:ui:release)`. Never ship Angular's bare `ngsw-worker.js` — `bun run build` wraps it with `service-worker/protected-api-worker.js` so `/api` and `/hubs` bypass cache.

**Mobile** (`src/mobile/android/intelibill_mobile`, Flutter Android only, `.flutter-version` `3.44.2`):
- Setup & codegen: `flutter pub get`, `./tool/codegen.sh` (commit `lib/**/*.g.dart`, `lib/**/*.freezed.dart`, `lib/src/core/localization/`)
- Verify: `flutter analyze`, `dart format --set-exit-if-changed .`, `flutter test --coverage`, `flutter build apk --debug`

**CI scope**: `.github/workflows/deploy.yml` builds and tests only the projects a change can affect. `.github/scripts/detect-changed-projects.sh` owns the path-to-project mapping (tested by `.github/scripts/tests/detect-changed-projects.test.sh`); when you add a project, a test root or a shared path, update both and `workflow-contracts.test.sh`. Mobile, barcode, HSN and infrastructure have their own path-filtered workflows.

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
- Angular 21 standalone components, NgRx root/feature stores, PrimeNG + shared `@intelibill/ui` and `@intelibill/api` workspace packages, Transloco i18n (`src/frontend/intelibill/public/assets/i18n`).
- Mobile: feature-first clean architecture (domain-independent), Riverpod codegen, GoRouter, Dio, Freezed/Json Serializable, Very Good Analysis.

## Maintainability Rules (All Code)

Read `.agents/rules/maintainability.md` (full rules and checklist) before writing or reviewing code. Hard limits:
- One statement per line; lines ≤ 140 chars; wrapped argument lists one per line.
- Methods ≤ 60 lines (never > 100), split into named steps; source files ≤ 400 lines.
- ≤ 10 injected dependencies per class (never > 12): move cohesive work into a collaborator (`patterns.md` §17), no pass-through wrappers. Locks, fences, transactions and saves stay in the handler's top-level method, in lock order.
- One definition and one message per error code, in `Errors`; no inline `Error.X("Code", …)` outside Domain. Check `IsError` before `.Value`.
- Application never names EF Core or Npgsql types: use `IUnitOfWork.IsConcurrencyConflict` / `IsWriteFailure`.
- Web: `OnPush`, no bare `.subscribe(`, no `any`, components ≤ 300 lines. Mobile: Freezed state, controllers ≤ 400 lines.
- Refactors change no behaviour and no test assertion; behaviour changes get their own commit. No dead, template, cache or generated files.

## Security, Tenancy & Runtime Invariants

- **Multi-shop RLS**: session interceptor sets `app.current_user_id` and `app.active_shop_id`; tenant tables enforce PostgreSQL RLS.
- **Auth & sessions**: verified email/phone credentials only (no external IdP). Every access JWT carries `active_shop_id` and `AuthSession` `sid` (tokens without `sid` are refused). Refresh tokens are single-use SHA-256 hashes chained per session; roles are `Owner`, `Manager`, `Staff`.
- **Rate limiting**: `RateLimitAttribute` / `RateLimitFilter` over `IAtomicRateLimitStore` (`rate_limit_buckets`); auth endpoints answer `503` when the store is down.
- **Safe logging & errors**: Serilog sinks sit behind `LogEventRedactor` (`Intelibill.Shared.Web`). Never log exception messages, tokens, links, or DTOs by hand. Unhandled/API errors answer `{type,title,status,code,traceId,errors?}` via `SafeErrorFactory` and `SafeDiagnosticWriter`.
- **Tests**: Testcontainers real PostgreSQL via Docker; integration classes run in parallel on per-class template databases (`SerialIntegrationTestGroup` for serial-only classes).

## Environment & Tech Stack

- **.NET**: SDK pinned in `global.json` (`10.0.105`, `latestMinor` roll-forward); ASP.NET Core 10, C# latest, EF Core 10, Npgsql, Wolverine 5.24, FluentValidation 12, ErrorOr 2.0, xUnit 2.9.
- **Database**: PostgreSQL (`src/backend/intelibill/Intelibill.Api/appsettings.Development.json` or `docker compose up -d`; `DatabaseOptions` in `Intelibill.Infrastructure/Options/`). `appsettings.json` is intentionally empty.
- **Web tests**: Vitest + Playwright.

## NuGet Packages & Supporting Services

- **Supporting services** (`barcode-master-api`, `hsn-finder-ai` under `src/backend/` and `tests/backend/`): separate deployables with their own solutions (`BarcodeAPI.sln`, `Sankra.HsnFinder.sln`), also listed in the root `Intelibill.slnx` but not in the merchant or licensing solutions; a new project there must be added to the root solution (`ProjectBoundaryAssertions` enforces it). Their local `Directory.Build.props` / `Directory.Packages.props` disable root CPM: keep those copies identical and **never** add service packages to the root `Directory.Packages.props`.

## CodeGraph

`.codegraph/` holds the code knowledge graph. Use `codegraph_context` first for broad tasks, `codegraph_search` / `codegraph_node` for symbols, `codegraph_callers` / `codegraph_callees` to trace execution, `codegraph_impact` before changing shared symbols; `rg` and file reads for config and docs. If `.codegraph/` is missing, ask before running `codegraph init -i`.

## Documentation Index (Read Before Editing a Project)

Docs are per project. Read **only the row for the project you are working in**; open another project's docs only when your change crosses into it.

| Working in | Read |
|---|---|
| `src/backend/intelibill/` (merchant API), `tests/backend/intelibill/` | `src/backend/intelibill/README.md`, `src/backend/intelibill/architecture_doc/architecture.md` (tenancy, auth, financial ops, credit, returns, stock/purchase receipts, posting periods, offline sync/reconciliation, locking orders, integration parallelism), `src/backend/intelibill/architecture_doc/patterns.md` |
| `src/backend/licensing/`, `tests/backend/licensing/`, `tests/contracts/licensing/` | `src/backend/licensing/README.md`, `src/backend/licensing/architecture_doc/architecture.md`, `docs/licensing-rollout.md` |
| `src/backend/shared/`, `tests/backend/shared/` | `src/backend/shared/README.md`, `src/backend/shared/architecture_doc/architecture.md` |
| `src/backend/barcode-master-api/`, `tests/backend/barcode-master-api/` | `src/backend/barcode-master-api/AGENTS.md`, `README.md` and `architecture_doc/architecture.md` in that folder |
| `src/backend/hsn-finder-ai/` | `src/backend/hsn-finder-ai/README.md`, `src/backend/hsn-finder-ai/architecture_doc/architecture.md` |
| `src/frontend/intelibill/` (merchant web) | `src/frontend/intelibill/README.md`, `src/frontend/intelibill/architecture_doc/architecture.md` (shop context transitions, protected API service worker, offline pack & printing, shared styles), `src/frontend/intelibill/architecture_doc/patterns.md` |
| `src/frontend/licensing/` (admin web) | `src/frontend/licensing/README.md`, `src/frontend/licensing/architecture_doc/architecture.md` |
| `src/frontend/shared/` (`@intelibill/ui`, `@intelibill/api`) | `src/frontend/shared/README.md`, `src/frontend/shared/architecture_doc/architecture.md` |
| `src/mobile/android/intelibill_mobile/` | `README.md` and `architecture_doc/architecture.md` in that folder |
| `.tofu/`, `.github/workflows/infra-*.yml` | `.tofu/README.md`, `.tofu/architecture_doc/architecture.md`, `docs/infrastructure-decisions.md`, `docs/infrastructure-implementation-guide.md` |

Repo-wide: `README.md` (overview, local setup, Docker stack) and `docs/` (auth setup, rollout, operations, security notes that span projects). Tests: `tests/backend/<project>/`. Root `Directory.Build.props`, `Directory.Packages.props`, `global.json`: MSBuild, CPM, SDK pin.

When you change a project's behaviour or structure, update that project's README and `architecture_doc/` in the same change. Do not recreate a root `architecture_doc/`.
