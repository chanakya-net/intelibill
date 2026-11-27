# intelibill

AI-powered inventory management and billing for multi-shop merchants: a merchant API and web app, an Android app, a licensing service with its admin web app, and two supporting lookup services.

## Build Status

| | Backend | Frontend | Mobile |
|---|---|---|---|
| Build | [![Backend CI](.github/badges/backend-build.svg)](https://github.com/chanakya-net/intelibill/actions/workflows/deploy.yml) | [![Frontend CI](.github/badges/frontend-build.svg)](https://github.com/chanakya-net/intelibill/actions/workflows/deploy.yml) | [![Mobile CI](.github/badges/mobile-build.svg)](https://github.com/chanakya-net/intelibill/actions/workflows/mobile-main-ci.yml) |
| Tests | [![Tests](.github/badges/tests.svg)](https://github.com/chanakya-net/intelibill/actions/workflows/deploy.yml) | [![Frontend Tests](.github/badges/frontend-tests.svg)](https://github.com/chanakya-net/intelibill/actions/workflows/deploy.yml) | [![Mobile Tests](.github/badges/mobile-tests.svg)](https://github.com/chanakya-net/intelibill/actions/workflows/mobile-main-ci.yml) |

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core 10, C# latest, .NET 10.0.105 |
| Database | PostgreSQL via Npgsql + EF Core 10 |
| Messaging / CQRS | Wolverine 5.24 |
| Validation | FluentValidation 12 |
| Error handling | ErrorOr 2.0 |
| Frontend | Angular 21 (client-rendered), PWA, Tailwind CSS 4, PrimeNG 21, NgRx 21, Transloco, Bun 1.3.11 |
| Infrastructure | OpenTofu on Azure, GitHub Actions |
| Mobile | Flutter 3.44.2, Android only (Riverpod, GoRouter, Dio, Freezed) |

## Repository Structure

```
intelibill/
├── src/
│   ├── backend/
│   │   ├── intelibill/             # Merchant API — Clean Architecture (Intelibill.slnx)
│   │   ├── licensing/              # Licensing service (Intelibill.Licensing.slnx)
│   │   ├── shared/                 # Shared backend libraries (Contracts, Security, Web)
│   │   ├── barcode-master-api/     # Product lookup by barcode (BarcodeAPI.sln)
│   │   └── hsn-finder-ai/          # AI HSN code lookup (Sankra.HsnFinder.sln)
│   ├── frontend/
│   │   ├── intelibill/             # Merchant Angular web app (Bun, PWA, NgRx, IndexedDB)
│   │   ├── licensing/              # Admin Angular web app
│   │   └── shared/                 # Shared web workspace packages (@intelibill/api, @intelibill/ui)
│   └── mobile/
│       └── android/
│           └── intelibill_mobile/  # Flutter Android
├── tests/
│   ├── backend/
│   │   ├── intelibill/             # Merchant unit & integration tests (Testcontainers)
│   │   ├── licensing/              # Licensing integration, browser & acceptance tests
│   │   ├── shared/                 # Cross-consumer regression tests
│   │   └── barcode-master-api/     # Barcode service tests
│   └── contracts/                  # Cross-client contract fixtures
├── .tofu/                      # OpenTofu infrastructure (Azure)
├── docs/                       # Cross-project setup, rollout, operations & security notes
├── scripts/                    # Database and developer scripts
├── docker-compose.yml          # Local database and full local stack
├── Intelibill.slnx             # Root solution: merchant, licensing, shared and their tests
├── Directory.Build.props       # Root MSBuild settings (warnings are errors)
├── Directory.Packages.props    # Central Package Management
├── package.json, bun.lock      # Bun workspaces and the single lockfile
└── global.json                 # .NET SDK pin
```

## Project Documentation

Every project keeps its own README and architecture docs next to its code. Start with the project you are working on; nothing project-specific lives at the repository root.

| Project | README | Architecture |
|---|---|---|
| Merchant API | [src/backend/intelibill](src/backend/intelibill/README.md) | [architecture](src/backend/intelibill/architecture_doc/architecture.md), [patterns](src/backend/intelibill/architecture_doc/patterns.md) |
| Licensing service | [src/backend/licensing](src/backend/licensing/README.md) | [architecture](src/backend/licensing/architecture_doc/architecture.md) |
| Shared backend libraries | [src/backend/shared](src/backend/shared/README.md) | [architecture](src/backend/shared/architecture_doc/architecture.md) |
| Barcode master API | [src/backend/barcode-master-api](src/backend/barcode-master-api/README.md) | [architecture](src/backend/barcode-master-api/architecture_doc/architecture.md) |
| HSN finder AI | [src/backend/hsn-finder-ai](src/backend/hsn-finder-ai/README.md) | [architecture](src/backend/hsn-finder-ai/architecture_doc/architecture.md) |
| Merchant web | [src/frontend/intelibill](src/frontend/intelibill/README.md) | [architecture](src/frontend/intelibill/architecture_doc/architecture.md), [patterns](src/frontend/intelibill/architecture_doc/patterns.md) |
| Admin web | [src/frontend/licensing](src/frontend/licensing/README.md) | [architecture](src/frontend/licensing/architecture_doc/architecture.md) |
| Shared web packages | [src/frontend/shared](src/frontend/shared/README.md) | [architecture](src/frontend/shared/architecture_doc/architecture.md) |
| Android app | [src/mobile/android/intelibill_mobile](src/mobile/android/intelibill_mobile/README.md) | [architecture](src/mobile/android/intelibill_mobile/architecture_doc/architecture.md) |
| Infrastructure | [.tofu](.tofu/README.md) | [architecture](.tofu/architecture_doc/architecture.md) |

Notes that span projects (auth setup, rollouts, operations, security) are in [docs/](docs). Conventions for AI coding agents, including which docs to read for which project, are in [AGENTS.md](AGENTS.md).

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.105+)
- PostgreSQL running locally (`docker compose up -d` starts one)
- [Bun](https://bun.sh/) 1.3.11 (never `npm` or `yarn`)
- Docker, for the local database, the full stack and integration tests

### Backend

```bash
dotnet build src/backend/intelibill/Intelibill.slnx

# Edit src/backend/intelibill/Intelibill.Api/appsettings.Development.json with DB credentials

dotnet ef database update \
  --project src/backend/intelibill/Intelibill.Infrastructure \
  --startup-project src/backend/intelibill/Intelibill.Api

dotnet run --project src/backend/intelibill/Intelibill.Api
```

API: `http://localhost:5277` — OpenAPI: `http://localhost:5277/openapi/v1.json` (dev only). More: [src/backend/intelibill/README.md](src/backend/intelibill/README.md).

### Frontend

```bash
cd src/frontend/intelibill
bun install
bun run start
```

Dev server: `http://localhost:4200`. More: [src/frontend/intelibill/README.md](src/frontend/intelibill/README.md).

### Mobile

Flutter Android (Android-only):

```bash
cd src/mobile/android/intelibill_mobile

# Setup and codegen
flutter pub get
./tool/codegen.sh

# Development
flutter analyze
dart format --set-exit-if-changed .
flutter test --coverage

# Build debug APK
flutter build apk --debug
```

See [src/mobile/android/intelibill_mobile/README.md](src/mobile/android/intelibill_mobile/README.md) for setup, architecture and testing details.

### Docker

```bash
# DB only (for local backend dev)
docker compose up -d

# Full stack: merchant API/web, licensing API/admin web, explicit migrations, Mailpit
docker compose --profile full up -d --build
```

The full stack prepares the databases and applies both schemas itself, in order,
before either API starts: `db-bootstrap` (roles, licensing database, connection
isolation) → `merchant-migrate` / `licensing-migrate` (EF migration bundles,
run as each database's owner) → `db-runtime-role` / `licensing-runtime-role`
(data grants) → `backend` / `licensing-api` → `frontend` / `admin-frontend`.
Neither API migrates or creates anything at startup, and each connects as a
restricted login (`intelibill_app`, `intelibill_licensing_app`) that cannot open
the other's database, change schema or bypass row-level security. A failure in
one branch never holds the other back. Details, configuration and
troubleshooting: [docs/licensing-rollout.md](docs/licensing-rollout.md).

When you run the DB-only profile and develop the API on the host, apply the
merchant schema yourself with the `dotnet ef database update` command under
[Backend](#backend) above, pointed at `localhost:5432`.

Identity email is captured locally by the `mailpit` service rather than
delivered: registration and password-reset messages appear at
`http://localhost:8025` and never leave the machine. See
[docs/auth-email-setup.md](docs/auth-email-setup.md).

The stack also sets `RateLimiting__RelaxEndpointPolicies`, so the auth endpoints
use the global default (100 per minute) instead of their own hardened figures —
sign-up is otherwise 5 requests per 15 minutes shared by every browser behind the
web proxy, which stops you testing the flow. The API refuses to start with that
setting in any environment but Development or Testing.

| Service  | URL |
|---|---|
| Merchant API | `http://127.0.0.1:5277` |
| Merchant web | `http://127.0.0.1:4000` |
| Licensing API | `http://127.0.0.1:5278` |
| Admin web | `http://127.0.0.1:4001` |
| Mail capture | `http://127.0.0.1:8025` |
| PostgreSQL | `localhost:5432` |

```bash
# Rebuild after code changes (also re-runs migrations and grants)
docker compose --profile full up -d --build

# Stop, keeping data (add -v only if you mean to wipe both databases)
docker compose --profile full down
```

## Deployment

Infrastructure is defined with OpenTofu under [.tofu](.tofu/README.md) and targets Azure Container Apps. See the [infrastructure architecture](.tofu/architecture_doc/architecture.md) and the [implementation guide](docs/infrastructure-implementation-guide.md) for the current status.

## License

This project is licensed under the GNU Affero General Public License v3.0. See [LICENSE](LICENSE) for details.
